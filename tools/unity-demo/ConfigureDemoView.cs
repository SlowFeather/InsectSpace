// Editor-local Game view preference, not a Player or project resolution setting.
// Reflection is limited to Unity's internal GameView sizing API; feature-check every entry point.
var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic |
    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static;
var assembly = typeof(UnityEditor.Editor).Assembly;
var sizesType = assembly.GetType("UnityEditor.GameViewSizes");
var sizeType = assembly.GetType("UnityEditor.GameViewSize");
var modeType = assembly.GetType("UnityEditor.GameViewSizeType");
var viewType = assembly.GetType("UnityEditor.GameView");
if (sizesType == null || sizeType == null || modeType == null || viewType == null)
    return "Set the Game view manually to 1440x900 (16:10).";
var singleton = typeof(UnityEditor.ScriptableSingleton<>).MakeGenericType(sizesType);
var sizes = singleton.GetProperty("instance", flags)?.GetValue(null);
var group = sizesType.GetProperty("currentGroup", flags)?.GetValue(sizes);
var selected = viewType.GetProperty("selectedSizeIndex", flags);
var countMethod = group?.GetType().GetMethod("GetTotalCount", flags);
var getSize = group?.GetType().GetMethod("GetGameViewSize", flags);
var addSize = group?.GetType().GetMethod("AddCustomSize", flags);
if (group == null || selected == null || countMethod == null || getSize == null || addSize == null)
    return "Set the Game view manually to 1440x900 (16:10).";
int count = (int)countMethod.Invoke(group, null);
int index = -1;
for (int i = 0; i < count; i++)
{
    var size = getSize.Invoke(group, new object[] { i });
    if ((int)sizeType.GetProperty("width", flags).GetValue(size) == 1440 &&
        (int)sizeType.GetProperty("height", flags).GetValue(size) == 900)
    { index = i; break; }
}
if (index < 0)
{
    var mode = System.Enum.Parse(modeType, "FixedResolution");
    var size = System.Activator.CreateInstance(sizeType, flags, null,
        new object[] { mode, 1440, 900, "InsectSpace Demo" }, null);
    addSize.Invoke(group, new[] { size });
    index = count;
}
var window = UnityEditor.EditorWindow.GetWindow(viewType);
selected.SetValue(window, index);
window.Focus(); window.Repaint();
return "Game view: 1440x900 / 16:10. Maximize the Game tab for a presentation.";
