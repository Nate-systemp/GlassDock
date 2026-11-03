using System.Xml;
using System.Xml.Linq;
namespace GlassDock.Core.Materials;

public static class UtilitySvg
{
    public static string Tint(string svg, uint color)
    {
        using var reader = XmlReader.Create(new StringReader(svg), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        var document = XDocument.Load(reader);
        var root = document.Root ?? throw new FormatException("Missing SVG root.");
        if (root.Name.LocalName != "svg") throw new FormatException("Invalid SVG root.");
        var hex = $"#{color & 0xFFFFFF:X6}";
        foreach (var element in root.DescendantsAndSelf())
        {
            if (element.Name.LocalName is "script" or "image" or "use" or "foreignObject")
                throw new FormatException("External SVG content is not supported.");
            foreach (var attribute in element.Attributes().ToArray())
            {
                if (attribute.Name.LocalName is "fill" or "stroke" && attribute.Value != "none") attribute.Value = hex;
                if (attribute.Name.LocalName is "style" or "href") attribute.Remove();
            }
        }
        root.SetAttributeValue("fill", hex);
        return document.ToString(SaveOptions.DisableFormatting);
    }
}
