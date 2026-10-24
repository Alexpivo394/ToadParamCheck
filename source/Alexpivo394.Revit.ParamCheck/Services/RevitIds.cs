using Autodesk.Revit.DB;

namespace Alexpivo394.Revit.ParamCheck.Services;

public static class RevitIds
{
    /// <summary>Число из <see cref="ElementId" /> во всех версиях: <c>Value</c> с Revit 2024, <c>IntegerValue</c> до него.</summary>
    public static long Value(ElementId id)
    {
#if REVIT2024_OR_GREATER
        return id.Value;
#else
        return id.IntegerValue;
#endif
    }
}
