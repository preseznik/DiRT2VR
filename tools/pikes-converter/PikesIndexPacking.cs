using System.Buffers.Binary;
using EgoEngineLibrary.Graphics.Pssg;
using EgoEngineLibrary.Graphics.Pssg.Elements;

// Pike's only: index count is unrelated to the number of addressable vertices.
internal static class PikesIndexPacking
{
    internal static (int Buffers,long BytesSaved) Apply(PssgFile file)
    {
        int buffers=0;long saved=0;
        foreach(var source in file.Elements<PssgRenderIndexSource>().Where(s=>s.Format=="uint"))
        {
            var data=source.ChildElements.OfType<PssgIndexSourceData>().Single();
            byte[] before=data.Value;
            if(before.LongLength!=source.Count*4L)throw new InvalidDataException("Invalid index payload: "+source.Id);
            uint maximum=0;
            for(int i=0;i<before.Length;i+=4)maximum=Math.Max(maximum,BinaryPrimitives.ReadUInt32BigEndian(before.AsSpan(i,4)));
            if(maximum>ushort.MaxValue)continue;
            byte[] after=new byte[before.Length/2];
            for(int i=0;i<after.Length;i+=2)BinaryPrimitives.WriteUInt16BigEndian(after.AsSpan(i,2),checked((ushort)BinaryPrimitives.ReadUInt32BigEndian(before.AsSpan(i*2,4))));
            for(int i=0;i<after.Length;i+=2)
                if(BinaryPrimitives.ReadUInt16BigEndian(after.AsSpan(i,2))!=BinaryPrimitives.ReadUInt32BigEndian(before.AsSpan(i*2,4)))
                    throw new InvalidDataException("Index readback mismatch: "+source.Id);
            data.Value=after;source.Format="ushort";source.MaximumIndex=maximum;
            buffers++;saved+=before.Length-after.Length;
        }
        return(buffers,saved);
    }
}
