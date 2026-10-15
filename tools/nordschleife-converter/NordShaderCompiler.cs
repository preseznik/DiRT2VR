using System.Runtime.InteropServices;
using System.Text;
using System.Buffers.Binary;
using System.Xml.Linq;

namespace DiRT2VR.Nordschleife;

internal static class NordShaderCompiler
{
    [DllImport("d3dcompiler_47.dll",CallingConvention=CallingConvention.StdCall)]
    static extern int D3DCompile(byte[] source,nuint size,[MarshalAs(UnmanagedType.LPStr)]string name,IntPtr defines,IntPtr include,
        [MarshalAs(UnmanagedType.LPStr)]string entry,[MarshalAs(UnmanagedType.LPStr)]string profile,uint flags,uint effectFlags,out IntPtr code,out IntPtr errors);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate IntPtr Pointer(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate nuint Size(IntPtr self);
    static byte[] Bytes(IntPtr blob)
    {
        var table=Marshal.ReadIntPtr(blob);
        var pointer=Marshal.GetDelegateForFunctionPointer<Pointer>(Marshal.ReadIntPtr(table,3*IntPtr.Size));
        var size=Marshal.GetDelegateForFunctionPointer<Size>(Marshal.ReadIntPtr(table,4*IntPtr.Size));
        var bytes=new byte[checked((int)size(blob))];Marshal.Copy(pointer(blob),bytes,0,bytes.Length);return bytes;
    }
    internal static byte[] Compile(string source,string name,string profile="ps_5_0")
    {
        var bytes=Encoding.UTF8.GetBytes(source);IntPtr code=IntPtr.Zero,errors=IntPtr.Zero;
        try
        {
            int hr=D3DCompile(bytes,(nuint)bytes.Length,name,IntPtr.Zero,IntPtr.Zero,"main",profile,1u<<12,0,out code,out errors);
            if(hr<0)throw new InvalidDataException("Nord shader compile: "+(errors==IntPtr.Zero?hr.ToString():Encoding.UTF8.GetString(Bytes(errors))));
            return Bytes(code);
        }
        finally {if(code!=IntPtr.Zero)Marshal.Release(code);if(errors!=IntPtr.Zero)Marshal.Release(errors);}
    }
    // D3D11 stage linkage uses matching registers as well as semantic names.
    // Successful compilation/CreateShader alone does not validate a shader pair.
    internal static void ValidateLinkage(byte[] vertex,byte[] pixel,string name)
    {
        var outputs=Signature(vertex,"OSGN");
        foreach(var input in Signature(pixel,"ISGN").Where(i=>i.Used!=0&&i.Name!="SV_IsFrontFace"))
            if(!outputs.Any(o=>o.Name==input.Name&&o.Index==input.Index&&o.Register==input.Register&&(o.Mask&input.Used)==input.Used))
                throw new InvalidDataException($"Shader linkage mismatch in {name}: {input.Name}{input.Index} at register {input.Register}.");
    }
    internal static HashSet<string> TextureBindings(byte[] bytes)
    {
        int U(int at)=>checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at,4)));
        int start=Enumerable.Range(0,U(28)).Select(i=>U(32+i*4)).Single(i=>Encoding.ASCII.GetString(bytes,i,4)=="RDEF")+8;
        var result=new HashSet<string>(StringComparer.Ordinal);
        for(int i=0;i<U(start+8);i++)
        {
            int at=start+U(start+12)+i*32;if(U(at+4)!=2)continue;
            int name=start+U(at),end=Array.IndexOf(bytes,(byte)0,name);result.Add(Encoding.ASCII.GetString(bytes,name,end-name));
        }
        return result;
    }
    // EGO builds the input layout from the group's stream declarations, not
    // from the buffers alone. A missing declaration makes CreateInputLayout fail.
    internal static void ValidateStreams(XElement group,byte[] vertex)
    {
        bool program=group.Name=="SHADERPROGRAMCODE";
        var streams=group.Elements(program?"CGSTREAM":"SHADERSTREAMDEFINITION").Select(e=>(string)e.Attribute(program?"cgStreamName":"name")!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if((int?)group.Attribute(program?"streamCount":"parameterStreamCount")!=streams.Count)throw new InvalidDataException("Shader stream count mismatch.");
        foreach(var input in Signature(vertex,"ISGN").Where(i=>i.Used!=0&&!i.Name.StartsWith("SV_",StringComparison.OrdinalIgnoreCase)))
            if(!streams.Contains(input.Name+input.Index)&&!(input.Index==0&&streams.Contains(input.Name)))
                throw new InvalidDataException($"Missing shader stream in {group.Attribute("id")}: {input.Name}{input.Index}.");
    }
    static List<(string Name,int Index,int Register,int Mask,int Used)> Signature(byte[] bytes,string kind)
    {
        int U(int at)=>checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at,4)));
        int chunk=Enumerable.Range(0,U(28)).Select(i=>U(32+i*4)).Single(i=>Encoding.ASCII.GetString(bytes,i,4)==kind)+8;
        var result=new List<(string,int,int,int,int)>();
        for(int i=0;i<U(chunk);i++)
        {
            int row=chunk+8+i*24,at=chunk+U(row),end=Array.IndexOf(bytes,(byte)0,at);
            result.Add((Encoding.ASCII.GetString(bytes,at,end-at),U(row+4),U(row+16),bytes[row+20],bytes[row+21]));
        }
        return result;
    }
}
