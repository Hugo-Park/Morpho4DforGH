using System.Collections.Generic;
using Rhino.Geometry;
using System.Linq;
using System;
using Grasshopper.Kernel.Types;
using System.Drawing;
using Morpho4D.Models;
using Morpho4D.Solver;

namespace Morpho4D.Models
{
    public abstract class Material
    {
        /*재료의 기본 정보 변수들을 MaterialBase 구조체로 묶음*/
        public struct MaterialBase
        {
            public string materialName { get; set; } // 재료 이름
            public Color previewColor { get; set; } // preview 색상
            public double youngsModulus { get; set; } // 재료의 강성
            public double poissonRatio { get; set; } // 포아송 비
            public MaterialBase(string name, Color color, double youngsMod, double poisson)
            {
                materialName = name;
                previewColor = color;
                youngsModulus = youngsMod;
                poissonRatio = poisson;
            }
        }
        protected MaterialBase materialBase;
        public Material(MaterialBase materialBase)
        {
            this.materialBase = materialBase;
        }

        /// <summary>
        /// 추상함수 선언 -> Material이 가진 속성 중 하나를 업데이트 함.
        /// </summary>
        /// <param name="voxel"></param>
        /// <param name="stimulus">현재 자극 종류</param>
        /// <param name="t">solver 컴포넌트에 입력하는 시뮬레이션 상태 시간 ex) t = 30 -> 30초가 지난 상태의 시뮬레이션 형태 도출</param>
        public abstract void evaluateState(MorphoSolver solver, VoxelCell voxel, Stimulus stimulus, double t);

        public string getMaterialName()
        {
            return this.materialBase.materialName;
        }
        public Color getPreviewColor()
        {
            return this.materialBase.previewColor;
        }
    }

    public class SmpMat : Material
    {
        public double glassTransTemp { get; set; } // 유리전이온도
        public double maxSwellingRatio { get; set; } // 최대 열팽창률 -> smp에 불필요
        public double minSwellingRatio { get; set; } // 최소 열팽창률 -> smp에 불필요

        /*Sigmoid 함수 구현 변수*/
        public double glassyModulus { get; set; }
        public double rubberyModulus { get; set; }
        public double steepness { get; set; }

        /*Sigmoid 함수 구현*/
        public double calculateSigmoid(double temp)
        {
            double exponent = steepness * (temp - glassTransTemp);
            return rubberyModulus + (glassyModulus - rubberyModulus) / (1 + Math.Exp(exponent));
        }
        public override void evaluateState(MorphoSolver solver, VoxelCell voxel, Stimulus stimulus, double t)
        {
            double temp = stimulus.temperature;
            voxel.currentTemp = temp;

            double currentE = calculateSigmoid(temp);
            voxel.currentYoungsModulus = currentE;  // G1: 0.01 덮어쓰기 완전 제거

            // activationFraction: (Eg - E(T)) / (Eg - Er) — 온도 상승 시 1→0
            double denom = this.glassyModulus - this.rubberyModulus;
            double frac = (Math.Abs(denom) < 1e-9) ? 0.0 : (this.glassyModulus - currentE) / denom;
            voxel.activationFraction = Math.Max(0.0, Math.Min(1.0, frac));

            voxel.expansionForce = 1.0;
            // G1: hinge targetAngle 고정 로직 완전 제거
        }

        /*생성자*/
        public SmpMat(MaterialBase mBase, double minSwelling, double maxSwelling, SigmoidModel sigmoidModel)
            : base(mBase)
        {
            this.maxSwellingRatio = maxSwelling;
            this.minSwellingRatio = minSwelling;

            if (sigmoidModel != null)
            {
                this.glassTransTemp = sigmoidModel.Tg;
                this.glassyModulus = sigmoidModel.Eg;
                this.rubberyModulus = sigmoidModel.Er;
                this.steepness = sigmoidModel.k;
                this.materialBase.youngsModulus = calculateSigmoid(25.0); // 상온 기준으로 한번 업데이트 해줌
            }

        }
    }

    public class SigmoidModel
    {
        public double Tg { get; set; }
        public double Eg { get; set; }
        public double Er { get; set; }
        public double k { get; set; }
        public SigmoidModel(double tg, double eg, double er, double k)
        {
            this.Tg = tg;
            this.Eg = eg;
            this.Er = er;
            this.k = k;
        }
    }

    public class HydrogelMat : Material
    {
        public double maxSwellingRatio { get; set; } // 최대 팽창 비율
        public double minSwellingRatio { get; set; } // 최소 팽창 비율
        public double osmoticPressure { get; set; } // 삼투압

        /*Fick의 확산법칙 구현 변수*/
        public double diffusionCoefficient { get; set; } // 확산 계수
        public double maxHydration { get; set; } // 가질 수 있는 최대 농도
        public double saturationLimit { get; set; } // 표면 농도

        /*Fick의 확산법칙 구현*/
        public double calculateAnalyticalDiffusion(double distance, double time, double D, double Cs)
        {
            if (time <= 0) { return 0; }
            if (distance < 0) { return 0; }
            if (distance == 0) { return Cs; }

            double argument = distance / (2.0 * Math.Sqrt(D * time));
            double hydration = Cs * Erfc(argument);

            return Math.Min(hydration, maxHydration);
        }
        private double Erfc(double x) // 오차 함수
        {
            double t = 1.0 / (1.0 + 0.5 * Math.Abs(x));
            double ans = t * Math.Exp(-x * x - 1.26551223 + t * (1.00002368 + t * (0.37409196 +
                        t * (0.09678418 + t * (-0.18628806 + t * (0.27886807 +
                        t * (-1.13520398 + t * (1.48851587 + t * (-0.82215223 +
                        t * 0.17087277)))))))));

            return x >= 0 ? ans : 2.0 - ans;
        }
        public override void evaluateState(MorphoSolver solver, VoxelCell voxel, Stimulus stimulus, double t)
        {
            voxel.currentTemp = 25.0;
            double tolerance = 1e-4;

            double x = (voxel.distanceFromSurface <= tolerance) ? (voxel.voxelSize * 0.5) : voxel.distanceFromSurface;
            double hydration = calculateAnalyticalDiffusion(x, t, this.diffusionCoefficient, this.saturationLimit);
            voxel.currentHydration = hydration;
            voxel.expansionForce = 1.0 + (hydration * (this.maxSwellingRatio - 1.0));
            voxel.currentYoungsModulus = this.materialBase.youngsModulus;
            voxel.activationFraction = 0.0;

            /*
            foreach(Hinge h in voxel.hingeIndices)
            {
                h.targetAngle = Math.PI; // Hydrogel은 복셀간 거리 변화만 존재, 곡률 변화 없음
            }
            */
        }

        /*생성자*/
        public HydrogelMat(MaterialBase mBase, double maxSwelling, double minSwelling, double osmoPressure, FicksModel diffusionModel)
            : base(mBase)
        {
            this.maxSwellingRatio = maxSwelling;
            this.minSwellingRatio = minSwelling;
            this.osmoticPressure = osmoPressure;

            if (diffusionModel != null)
            {
                this.diffusionCoefficient = diffusionModel.D;
                this.maxHydration = diffusionModel.HMax;
                this.saturationLimit = diffusionModel.Cs;
            }
        }
    }

    public class FicksModel
    {
        public double D { get; set; }
        public double HMax { get; set; }
        public double Cs { get; set; }
        public FicksModel(double d, double hmax, double cs)
        {
            this.D = d;
            this.HMax = hmax;
            this.Cs = cs;
        }
    }

    public class PassiveMat : Material
    {
        public double thermalSoftening { get; set; } = 0.0;
        public override void evaluateState(MorphoSolver solver, VoxelCell voxel, Stimulus stimulus, double t)
        {
            voxel.currentTemp = stimulus.temperature;
            voxel.expansionForce = 1.0;
            voxel.activationFraction = 0.0;
            double drop = thermalSoftening * Math.Max(0.0, stimulus.temperature - 25.0);
            voxel.currentYoungsModulus = Math.Max(0.01, this.materialBase.youngsModulus - drop);
        }
        public PassiveMat(MaterialBase mBase, double thermalSoftening = 0.0) : base(mBase)
        {
            this.thermalSoftening = thermalSoftening;
        }
    }
}