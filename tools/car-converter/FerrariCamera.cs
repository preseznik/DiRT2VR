using System.Xml;

namespace DiRT2VR.CustomCars;

internal static class FerrariCamera
{
    internal static void Stabilize(XmlDocument document)
    {
        var head = document.SelectSingleNode("//View[@ident='head-cam']") ?? throw new InvalidDataException("Missing head camera");
        void Set(string name, string value)
        {
            var parameter = (XmlElement)(head.SelectSingleNode($"Parameter[@name='{name}']") ?? throw new InvalidDataException($"Missing camera parameter {name}"));
            parameter.SetAttribute("value", value);
        }
        // Retain positive inertia/damping constants; suppress the motion amplitudes.
        Set("headBuffeting", "false");
        Set("maxHeadOffset", "0");
        Set("maxBodyOffset", "0");
        foreach (XmlElement parameter in head.SelectNodes("AccelerationBasedShake/Parameter[@type='scalar']")!)
            if (parameter.GetAttribute("name").Contains("Shake", StringComparison.OrdinalIgnoreCase)) parameter.SetAttribute("value", "0");
    }

}
