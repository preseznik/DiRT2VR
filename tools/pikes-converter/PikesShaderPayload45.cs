using System.Xml.Linq;
using DiRT2VR.Nordschleife;
using EgoEngineLibrary.Graphics.Pssg;

internal static class PikesShaderPayload45
{
    internal static int Validate(PssgFile pack)
    {
        int count=0;
        foreach(var group in pack.Elements<PssgElement>().Where(e=>e.Name=="SHADERGROUP"&&e.GetAttributeValue<string>("id").StartsWith("pikes_canopy_",StringComparison.Ordinal))){
            int inputs=group.GetAttributeValue<int>("parameterCount"),streams=group.GetAttributeValue<int>("parameterStreamCount");
            if(group.ChildElements.Take(inputs).Any(e=>e.Name!="SHADERINPUTDEFINITION")||group.ChildElements.Skip(inputs).Take(streams).Any(e=>e.Name!="SHADERSTREAMDEFINITION"))throw new InvalidDataException("Native canopy group child order: "+group.GetAttributeValue<string>("id"));
        }
        foreach(var program in pack.Elements<PssgElement>().Where(e=>e.Name=="SHADERPROGRAM"))
        foreach(var code in program.ChildElements.Where(e=>e.Name=="SHADERPROGRAMCODE")){
            var block=code.ChildElements.Single(e=>e.Name=="SHADERPROGRAMCODEBLOCK");
            int expected=code.GetAttributeValue<int>("codeSize");
            if(block.Value.Length!=expected)throw new InvalidDataException($"Shader payload size mismatch: {program.GetAttributeValue<string>("id")}: declared {expected}, stored {block.Value.Length}");
            if(code.GetAttributeValue<string>("codeType")=="HLSLBinary" && !block.Value.AsSpan(0,Math.Min(4,block.Value.Length)).SequenceEqual("DXBC"u8))
                throw new InvalidDataException("Shader bytecode signature missing");
            count++;
        }
        if(count==0)throw new InvalidDataException("No shader programs found");
        return count;
    }
}
