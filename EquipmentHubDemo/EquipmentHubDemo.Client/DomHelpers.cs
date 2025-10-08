using System.Runtime.InteropServices.JavaScript;

namespace EquipmentHubDemo.Client;

internal static partial class DomHelpers
{
    [JSImport("hasSelector", "domHelpers")]
    public static partial bool HasSelector(string selector);
}
