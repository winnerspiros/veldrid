#r "nuget: Vortice.Vulkan, 3.2.1"
var t = typeof(Vortice.Vulkan.PFN_vkVoidFunction);
Console.WriteLine(t.FullName);
foreach (var m in t.GetMembers()) Console.WriteLine(m);
