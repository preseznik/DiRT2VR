using System.Runtime.InteropServices;
using System.Text;
using System.Xml.Linq;
using DiRT2VR.Nordschleife;

internal static class PikesShaderCompiler
{
    // Compile with the same DXBC generation imported by the native game.
    [DllImport("d3dcompiler_42.dll",CallingConvention=CallingConvention.StdCall)]
    static extern int D3DCompile(byte[] source,nuint size,[MarshalAs(UnmanagedType.LPStr)]string name,IntPtr defines,IntPtr include,[MarshalAs(UnmanagedType.LPStr)]string entry,[MarshalAs(UnmanagedType.LPStr)]string profile,uint flags,uint effectFlags,out IntPtr code,out IntPtr errors);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate IntPtr Pointer(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate nuint Size(IntPtr self);
    static byte[] Bytes(IntPtr blob){var table=Marshal.ReadIntPtr(blob);var pointer=Marshal.GetDelegateForFunctionPointer<Pointer>(Marshal.ReadIntPtr(table,3*IntPtr.Size));var size=Marshal.GetDelegateForFunctionPointer<Size>(Marshal.ReadIntPtr(table,4*IntPtr.Size));var bytes=new byte[checked((int)size(blob))];Marshal.Copy(pointer(blob),bytes,0,bytes.Length);return bytes;}
    internal static byte[] Compile(string source,string profile)
    {
        var bytes=Encoding.UTF8.GetBytes(source);IntPtr code=IntPtr.Zero,errors=IntPtr.Zero;
        try{int hr=D3DCompile(bytes,(nuint)bytes.Length,"Pikes source helicopter",IntPtr.Zero,IntPtr.Zero,"main",profile,1u<<12,0,out code,out errors);if(hr<0)throw new InvalidDataException(errors==IntPtr.Zero?hr.ToString():Encoding.UTF8.GetString(Bytes(errors)));return Bytes(code);}
        catch(DllNotFoundException e){throw new IOException("Pikes Peak conversion needs the DirectX End-User Runtimes (June 2010). Install or repair that runtime, then rebuild Pikes Peak.",e);}
        finally{if(code!=IntPtr.Zero)Marshal.Release(code);if(errors!=IntPtr.Zero)Marshal.Release(errors);}
    }
}
