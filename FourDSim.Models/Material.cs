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
        public double currentStimulus { get; set; } // 현재 가해지는 자극

        /*재료 공통 속성*/
        public virtual double youngsModulus { get; set; } // 재료의 강성
        public double expansionCoefficient { get; set; } // 팽창 계수
        public double poissonRatio { get; set; } // 포아송 비
        public Material(string name, Color color, double stimulus ,double youngsMod, double expansion, double poisson)
        {
            this.materialName = name;
            this.previewColor = color;
            this.currentStimulus = stimulus;
            this.youngsModulus = youngsMod;
            this.expansionCoefficient = expansion;
            this.poissonRatio = poisson;
        }

        /*특정 재료(smp, hydrogel) 속성은 상속을 통해 부여받는다*/
    }

    public class SmpMat : Material
    {

        public double glassTransTemp { get; set; } // 유리전이온도
        public double currentTemp { get; set; } // 현재 온도
        public double maxSwellingRatio { get; set; } // 최대 열팽창률
        public double minSwellingRatio { get; set; } // 최소 열팽창률

        /*Sigmoid 함수 구현 변수*/
        public double glassyModulus { get; set; }
        public double rubberyModulus { get; set; }
        public double steepness { get; set; }
        public double currentYoungsModulus;
        public override double youngsModulus { get => currentYoungsModulus; set => currentYoungsModulus = value; }

        /*Sigmoid 함수 구현*/
        public void updateSmpYoungsModulus(double currentTemp)
        {
            double exponent = steepness * (currentTemp - glassTransTemp);
            currentYoungsModulus = rubberyModulus + (glassyModulus - rubberyModulus) / (1 + Math.Exp(exponent));
        }

        /*생성자*/
        public SmpMat(string name, Color color, double stimulus, double youngsMod, double expansion, double poisson, double glassTemp, double curTemp)
            : base(name, color, stimulus, youngsMod, expansion, poisson)
        {
            this.glassTransTemp = glassTemp;
            this.currentTemp = curTemp;

            this.glassyModulus = youngsMod; // 일단 glassyModulus를 기본으로 설정
            this.rubberyModulus = youngsMod / 100.0; // 일단 100배 부드럽다고 설정
            this.steepness = 1.0;

            updateSmpYoungsModulus(this.currentTemp); // 오류방지를 위해 초기에 한번 값을 업데이트 해줌
        }
    }

    public class HydrogelMat : Material
    {
        public double maxSwellingRatio { get; set; } // 최대 팽창 비율
        public double minSwellingRatio { get; set; } // 최소 팽창 비율
        public double osmoiticPressure { get; set; } // 삼투압

        public double hydrationLevel { get; set; } // 농도/화학 포텐셜
        public double diffusionRate { get; set; } // Fick의 확산 법칙 사용
        
        /*생성자*/
        public HydrogelMat(string name, Color color, double stimulus, double youngsMod, double expansion, double poisson,double maxSwelling, double minSwelling, double osmoiPressure, double hydaranLev, double diffusRate)
            : base(name, color, stimulus, youngsMod, expansion, poisson)
        {
            maxSwellingRatio = maxSwelling;
            minSwellingRatio = minSwelling;
            osmoiticPressure = osmoiPressure;
            hydrationLevel = hydaranLev;
            diffusionRate = diffusRate;
        }
    }
}