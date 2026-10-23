using System.Security.Cryptography;
using System.Text.Json;
using DiRT2VR.Nordschleife;
using EgoEngineLibrary.Graphics.Pssg;
using EgoEngineLibrary.Graphics.Pssg.Elements;

internal static class ShaderFormats
{
    internal static int Validate(PssgFile file,bool repair=false)
    {
        int changed=0;
        foreach(var shader in file.Elements<PssgShaderInstance>())
        {
            var definitions=shader.GetShaderGroup().ChildElements.OfType<PssgShaderInputDefinition>().ToArray();
            foreach(var input in shader.Inputs)
            {
                var definition=definitions[input.ParameterId];
                if(definition.Type!="constant")continue;
                string format=definition.Format;
                int bytes=format switch {"float" or "int" or "bool"=>4,"float2"=>8,"float3"=>12,"float4"=>16,"float4x4"=>64,_=>throw new InvalidDataException("Unhandled shader constant format "+format)};
                if(input.Value.Length!=bytes)throw new InvalidDataException($"{shader.Id}/{definition.InputName}: {input.Value.Length} bytes, expected {bytes}");
                if(input.Format==format)continue;
                if(!repair||definition.InputName!="PhongColour"||input.Format!="float4"||format!="float3")
                    throw new InvalidDataException($"{shader.Id}/{definition.InputName}: {input.Format} != {format}");
                Console.WriteLine($"Repair {shader.Id}/{definition.InputName}: {input.Format} -> {format}, {bytes} bytes");
                input.Format=format;changed++;
            }
        }
        return changed;
    }
}
