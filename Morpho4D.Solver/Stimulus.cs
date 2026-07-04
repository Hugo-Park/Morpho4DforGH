using System.Collections.Generic;
using Rhino.Geometry;
using System.Linq;
using System;
using Grasshopper.Kernel.Types;
using System.Drawing;
using Morpho4D.Models;
using _Morpho4D;

namespace Morpho4D.Solver
{
    public class Stimulus
    {
        public string stimulusName { get; set; }
        public virtual double temperature { get; set; } = 25.0; // 일단 모든 자극울 상온으로 설정 -> 하위 클래스에서 수정 가능
        public bool isActive { set; get; } = true;

        // G8: SpatialFieldGenerator가 위치별 온도를 제공할 때 사용하는 필드
        public Func<Rhino.Geometry.Point3d, double> temperatureField { get; set; } = null;

        public Stimulus(string name, double temp)
        {
            this.stimulusName = name;
            this.temperature = temp;
        }

        /// <summary> G8: 위치별 온도. temperatureField가 설정돼 있으면 그것을, 아니면 전역 temperature를 반환. </summary>
        public virtual double TemperatureAt(Rhino.Geometry.Point3d pt)
        {
            return temperatureField != null ? temperatureField(pt) : this.temperature;
        }
    }

    public class HeatStim : Stimulus
    {
        public override double temperature { get; set; }
        public HeatStim(string name, double temp)
            : base(name, temp)
        {
            this.temperature = temp;
        }

    }
}