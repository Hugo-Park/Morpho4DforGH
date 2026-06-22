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

        // SpatialFieldGenerator 유틸리티가 주입하는 공간적 자극 필드.
        // null이면 기존처럼 scalar temperature 사용 (완전 하위호환).
        public Func<Point3d, double> temperatureField { get; set; } = null;

        /// <summary>
        /// 복셀 위치에서의 자극 온도를 반환. 공간 필드가 설정되어 있으면 위치 기반 값, 아니면 scalar temperature.
        /// </summary>
        public double TemperatureAt(VoxelCell voxel)
        {
            if (temperatureField != null) return temperatureField(voxel.currentPoint);
            return this.temperature;
        }

        public Stimulus(string name, double temp)
        {
            this.stimulusName = name;
            this.temperature = temp;
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