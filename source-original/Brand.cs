using System.Drawing;
using System.IO;
using System.Reflection;

internal static class Brand
{
    internal static Icon CreateIcon(int size)
    {
        using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ZapretGui.Icon"))
        using (Icon source = new Icon(stream, new Size(size, size)))
            return (Icon)source.Clone();
    }
    internal static Bitmap CreateImage(int size)
    {
        using (Icon icon = CreateIcon(size)) return icon.ToBitmap();
    }
}
