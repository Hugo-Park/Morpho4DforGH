using System.Collections.Generic;
using Rhino.Geometry;
using System.Linq;
using System;
using Grasshopper.Kernel.Types;
using System.Drawing;

namespace Morpho4D.Models
{
    public class Material
    {
        /*기본 정보*/
        public string materialName { get; set; } // 재료 이름
        public Color previewColor { get; set; } // preview 색상

        /*재료 공통 속성*/
        public virtual double youngsModulus { get; set; } // 재료의 강성
        public double poissonRatio { get; set; } // 포아송 비
        public Material(string name, Color color, double youngsMod, double poisson)
        {
            this.materialName = name;
            this.previewColor = color;
            this.youngsModulus = youngsMod;
            this.poissonRatio = poisson;
        }

        /*특정 재료(smp, hydrogel) 속성은 상속을 통해 부여받는다*/
    }

    public class SmpMat : Material
    {

        public double glassTransTemp { get; set; } // 유리전이온도
        public double maxSwellingRatio { get; set; } // 최대 열팽창률
        public double minSwellingRatio { get; set; } // 최소 열팽창률

        /*Sigmoid 함수 구현 변수*/
        public double glassyModulus { get; set; }
        public double rubberyModulus { get; set; }
        public double steepness { get; set; }
        private double defaultYoungsModulus;
        public override double youngsModulus
        {
            get => defaultYoungsModulus;
            set => defaultYoungsModulus = value;
        }

        /*Sigmoid 함수 구현(컴포넌트 분리 필요)*/
        public double updateSmpYoungsModulus(double currentTemp)
        {
            double exponent = steepness * (currentTemp - glassTransTemp);
            return rubberyModulus + (glassyModulus - rubberyModulus) / (1 + Math.Exp(exponent));
        }

        /*생성자*/
        public SmpMat(string name, Color color, double youngsMod, double poisson, double glassTemp)
            : base(name, color, youngsMod, poisson)
        {
            this.glassTransTemp = glassTemp;
            this.glassyModulus = youngsMod; // 일단 glassyModulus를 기본으로 설정
            this.rubberyModulus = youngsMod / 100.0; // 일단 100배 부드럽다고 설정
            this.steepness = 1.0;
            this.defaultYoungsModulus = updateSmpYoungsModulus(25.0); // 상온 기준으로 한번 업데이트 해줌
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
        public double diffusionRate { get; set; } // Fick의 확산 법칙 변수

        /*Fick의 확산법칙 구현(컴포넌트 분리 필요)*/
        public double updateHydrationLevel(double gradient, double deltaTime)
        {
            return this.diffusionRate * gradient * deltaTime;
        }

        /*생성자*/
        public HydrogelMat(string name, Color color, double youngsMod, double poisson, double maxSwelling, double minSwelling, double osmoPressure, double diffusRate)
            : base(name, color, youngsMod, poisson)
        {
            maxSwellingRatio = maxSwelling;
            minSwellingRatio = minSwelling;
            osmoticPressure = osmoPressure;
            diffusionRate = diffusRate;
        }
    }
}