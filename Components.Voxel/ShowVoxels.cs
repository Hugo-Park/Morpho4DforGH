using System;
using System.Collections.Generic;
using FourDSim.Models;
using Grasshopper;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace _4DPrintSim
{
  public class ShowVoxels : GH_Component
  {
    /// <summary>
    /// Each implementation of GH_Component must provide a public 
    /// constructor without any arguments.
    /// Category represents the Tab in which the component will appear, 
    /// Subcategory the panel. If you use non-existing tab or panel names, 
    /// new tabs/panels will automatically be created.
    /// </summary>
    public ShowVoxels()
      : base("Show Voxels", "SV",
        "Show converted Voxels",
        "4DPrintSim", "Voxel")
    {
    }

    /// <summary>
    /// Registers all the input parameters for this component.
    /// </summary>
    protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
    {
        pManager.AddGenericParameter("Voxels", "V", "Input Vocels", GH_ParamAccess.list);
    }

    /// <summary>
    /// Registers all the output parameters for this component.
    /// </summary>
    protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
    {
        pManager.AddMeshParameter("Mesh", "M", "Ouput Voxels(Mesh)", GH_ParamAccess.item); // Mesh 객체 하나로 합쳐서 내보냄
        pManager.AddPointParameter("Point", "P", "Output Voxels(Point)", GH_ParamAccess.list); // Mesh 객체들의 중심점
    }

    /// <summary>
    /// This is the method that actually does the work.
    /// </summary>
    /// <param name="DA">The DA object can be used to retrieve data from input parameters and 
    /// to store data in output parameters.</param>
    protected override void SolveInstance(IGH_DataAccess DA)
    {
        List<VoxelCellGoo> goos = new List<VoxelCellGoo>();

        if (!DA.GetDataList(0, goos)) { return ; }
        
        
    }

    /// <summary>
    /// Provides an Icon for every component that will be visible in the User Interface.
    /// Icons need to be 24x24 pixels.
    /// You can add image files to your project resources and access them like this:
    /// return Resources.IconForThisComponent;
    /// </summary>
    protected override System.Drawing.Bitmap Icon => null;

    /// <summary>
    /// Each component must have a unique Guid to identify it. 
    /// It is vital this Guid doesn't change otherwise old ghx files 
    /// that use the old ID will partially fail during loading.
    /// </summary>
    public override Guid ComponentGuid => new Guid("7C721802-72FB-4BCE-B06E-B3BBAE441DA8");
  }
}