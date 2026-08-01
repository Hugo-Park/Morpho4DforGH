using System;
using System.Collections.Generic;
using System.Drawing;
using Grasshopper;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Morpho4D.Models;

namespace _Morpho4D
{
    public class MaterialBaseComponent : GH_Component
    {
        public MaterialBaseComponent()
          : base("Material Base", "mB",
            "Define material's basic informations before creating specific materials",
            "Morpho4D", "02 Material")
        {
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("Name", "N", "Specific name or nickname of the material", GH_ParamAccess.item, "User Material");
            pManager.AddColourParameter("Preview Color", "PrvCol", "Preview color of the material", GH_ParamAccess.item, Color.Blue);
            pManager.AddNumberParameter("Young's Modulus", "Y's", "Young's modulus of the material", GH_ParamAccess.item, 1000.0);
            pManager.AddNumberParameter("Poisson's Ratio", "PR", "Poisson's ratio of the material", GH_ParamAccess.item, 0.4);
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Material Base", "mB", "Basic informations of the material", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string matName = "User Material";
            Color prevCol = Color.Blue;
            double youngs = 1000.0;
            double poissonR = 0.4;

            if (!DA.GetData(0, ref matName)) { return; }
            if (!DA.GetData(1, ref prevCol)) { return; }
            if (!DA.GetData(2, ref youngs)) { return; }
            if (!DA.GetData(3, ref poissonR)) { return; }

            Material.MaterialBase materialBase = new Material.MaterialBase(matName, prevCol, youngs, poissonR);

            DA.SetData(0, materialBase);
        }
        protected override System.Drawing.Bitmap Icon => _Morpho4D.IconLoader.Get("MaterialBaseComponent");

        public override Guid ComponentGuid => new Guid("4D9A74AE-1EDE-4098-BD0C-D3AD512706F0");
    }
}
