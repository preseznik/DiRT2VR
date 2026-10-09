using System.Runtime.InteropServices;
using System.Text;

internal static class MistyShaderCompiler
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
    internal static byte[] Compile(string source,string name)
    {
        var bytes=Encoding.UTF8.GetBytes(source);IntPtr code=IntPtr.Zero,errors=IntPtr.Zero;
        try
        {
            int hr=D3DCompile(bytes,(nuint)bytes.Length,name,IntPtr.Zero,IntPtr.Zero,"main","ps_5_0",1u<<12,0,out code,out errors);
            if(hr<0)throw new InvalidDataException("Misty shader compile: "+(errors==IntPtr.Zero?hr.ToString():Encoding.UTF8.GetString(Bytes(errors))));
            return Bytes(code);
        }
        finally {if(code!=IntPtr.Zero)Marshal.Release(code);if(errors!=IntPtr.Zero)Marshal.Release(errors);}
    }
}
