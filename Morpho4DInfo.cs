using System;
using System.Drawing;
using Grasshopper;
using Grasshopper.Kernel;

namespace _Morpho4D
{
  public class _Morpho4DInfo : GH_AssemblyInfo
  {
    public override string Name => "Morpho4D Info";

    //Return a 24x24 pixel bitmap to represent this GHA library.
    public override Bitmap Icon => null;

    //Return a short string describing the purpose of this GHA library.
    public override string Description => "This is 4D printing simulator for Grasshopper3D";

    public override Guid Id => new Guid("5e6e519a-00d8-46c7-b094-c67ec67703ae");

    //Return a string identifying you or your company.
    public override string AuthorName => "";

    //Return a string representing your preferred contact details.
    public override string AuthorContact => "";

    //Return a string representing the version.  This returns the same version as the assembly.
    public override string AssemblyVersion => GetType().Assembly.GetName().Version.ToString();
  }
}