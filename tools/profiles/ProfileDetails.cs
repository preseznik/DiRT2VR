using System.Buffers.Binary;

namespace DiRT2VR.Profiles;

public sealed class ProfileDetails
{
    static readonly HashSet<uint> StockEvents = [100,197,198,199,200,210,212,213,214,215,216,217,218,219,220,221,222,223,224,226,227,228,229,230,231,232,233,234,235,236,237,238,239,240,241,242,243,244,245,246,247,248,250,251,252,253,255,256,257,258,259,260,261,262,263,264,265,266,267,268,269,270,271,272,274,275,276,277,278,279,280,281,282,283,285,286,287,288,289,290,291,292,293,294,295,296,297,298,299,300,301,302,303,304,305,306,307,308,309,317];
    public string? Name { get; private set; }
    public int? Completion { get; private set; }
    public int? Level { get; private set; }
    public uint? Money { get; private set; }
    // Variant ownership is understood; visible garage/model filtering is not yet
    // validated. Never present the variant count as a claimed garage car count.
    public int? CarsOwned => null;
    public DateTime? LastSavedUtc { get; private set; }

    static byte[] Record(string directory, string name)
    {
        var path = Path.Combine(directory,name);
        if (new FileInfo(path).Length > Dirt2SaveContainer.MaxBytes) throw new InvalidDataException("Save record too large.");
        return Dirt2SaveContainer.Decode(File.ReadAllBytes(path)).Payload;
    }
    static bool Unreadable(Exception error) => error is IOException or InvalidDataException or
        UnauthorizedAccessException or System.Xml.XmlException or System.Text.DecoderFallbackException;

    public static ProfileDetails Inspect(string directory)
    {
        var result = new ProfileDetails();
        try
        {
            var path = Path.Combine(directory,"NXDSMWW");
            if (new FileInfo(path).Length > Dirt2SaveContainer.MaxBytes) throw new InvalidDataException();
            var xml = Dirt2SaveContainer.Decode(File.ReadAllBytes(path)).ReadProfileXml();
            result.Name = ((string?)xml.Root!.Element("Player_FirstName") + " " + (string?)xml.Root.Element("Player_LastName")).Trim();
            result.LastSavedUtc = File.GetLastWriteTimeUtc(path);
        } catch(Exception ex) when(Unreadable(ex)) { }
        try { (result.Level,result.Completion) = History(Record(directory,"PGRRLTKJNZI")); }
        catch(Exception ex) when(Unreadable(ex)) { }
        try
        {
            var cursor = new Cursor(Record(directory,"AGGRIC CZVKEKG WOM MIWC"));
            cursor.Array(4); int count = cursor.Count(); cursor.Array(8);
            cursor.Skip(count*4); cursor.UInt(); result.Money = cursor.UInt();
        } catch(Exception ex) when(Unreadable(ex)) { }
        return result;
    }
    static (int? level,int? completion) History(byte[] bytes)
    {
        var c = new Cursor(bytes);
        if(c.UInt()!=1337 || c.UInt()!=57 || c.UInt()!=15) throw new InvalidDataException("Unsupported history.");
        c.Skip(20*16); uint xp=c.UInt(); c.Skip(57*16-20*16-4);
        c.Skip(15*16+32+4);
        for(int i=0;i<3;i++) c.Array(4);
        for(int i=0;i<3;i++) c.Array(8);
        c.Array(24);
        (int size,int count)[] types=[(8,5),(44,200),(52,15),(52,15),(12,100),(1020,150),(52,25),(64,10),(16,70),(72,200)];
        int events=0,wins=0,unplayed=0;
        bool stockEvents=false;
        foreach(var (size,count) in types)
        {
            if(c.UInt()!=size) throw new InvalidDataException("Unsupported history collection.");
            var ids=new HashSet<uint>();
            for(int i=0;i<count;i++)
            {
                uint present=c.UInt();
                if(present>1) throw new InvalidDataException("Invalid history presence flag.");
                if(present==0) continue;
                if(!ids.Add(c.UInt())) throw new InvalidDataException("Duplicate history identity.");
                uint position=c.UInt(); c.Skip(size-8);
                if(size==72) {events++;if(position==0)wins++;if(position==uint.MaxValue)unplayed++;}
            }
            if(size==72) stockEvents=ids.SetEquals(StockEvents);
        }
        if(c.Remaining>=1024) throw new InvalidDataException("Unsupported history tail.");
        (uint xp,int level)[] knots=[(0,1),(6000,2),(30000,6),(51000,9),(63000,10),(75000,11),
            (94500,12),(114000,13),(231000,19),(270500,20),(428500,24),(468000,25),(38941000,999)];
        int? level=null;
        for(int i=0;i<knots.Length-1;i++)
            if(xp>=knots[i].xp && xp<knots[i+1].xp)
                level=knots[i].level+(int)((long)(xp-knots[i].xp)*(knots[i+1].level-knots[i].level)/(knots[i+1].xp-knots[i].xp));
        if(xp==38941000)level=999;
        // Only these extremes have been established against the game's UI.
        int? completion=!stockEvents || events!=100 ? null : wins==100 ? 100 : unplayed==100 ? 0 : null;
        return (level,completion);
    }
    sealed class Cursor(byte[] data)
    {
        int offset;
        public int Remaining=>data.Length-offset;
        public void Skip(int count)
        {
            if(count<0 || count>Remaining)throw new InvalidDataException("Truncated save record.");
            offset+=count;
        }
        public uint UInt() {int start=offset;Skip(4);return BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(start,4));}
        public int Count() {uint count=UInt();if(count>10000)throw new InvalidDataException("Save count exceeds bound.");return (int)count;}
        public void Array(int width)=>Skip(Count()*width);
    }
}
