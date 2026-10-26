using System.Numerics;
using DiRT2VR.Nordschleife;

internal static class PikesDistantForest
{
    // Keep every nearby placement, including the roadside canopy. Only shorter
    // trees in surrounded cells may fade after 600 m; edges and tall crowns remain.
    const float CellSize=32;
    static Cell CellAt(Vector3 p)=>new((int)MathF.Floor(p.X/CellSize),(int)MathF.Floor(p.Z/CellSize));
    internal static void Apply(Scene scene,string output)
    {
        var trees=scene.Visuals.Select((m,i)=>(Mesh:m,Index:i)).Where(p=>p.Mesh.Material.Shader==PikesTreeBillboards.Name).ToArray();
        var crowns=new Dictionary<Cell,float>();
        foreach(var (mesh,_) in trees)
        {
            var cell=CellAt(mesh.Positions[0]);
            float top=mesh.Positions[0].Y+mesh.TexCoords![1].Max(v=>v.Y);
            crowns[cell]=Math.Max(crowns.GetValueOrDefault(cell,float.MinValue),top);
        }
        int interior=0,selected=0;
        foreach(var (mesh,index) in trees)
        {
            var centre=mesh.Positions[0];var cell=CellAt(centre);
            float top=mesh.Positions[0].Y+mesh.TexCoords![1].Max(v=>v.Y);
            bool surrounded=true;float tallest=top;
            for(int x=-1;x<=1;x++)for(int z=-1;z<=1;z++)
                if(x!=0||z!=0){if(!crowns.TryGetValue(new(cell.X+x,cell.Z+z),out float neighbour))surrounded=false;else tallest=Math.Max(tallest,neighbour);}
            bool eligible=surrounded&&top<=tallest-2;
            if(eligible)interior++;
            bool thin=eligible&&(Hash(centre)&3)==0;if(thin)selected++;
            scene.Visuals[index]=mesh with{TexCoords=[mesh.TexCoords[0],mesh.TexCoords[1],Enumerable.Repeat(new Vector2(thin?1:0,0),mesh.Positions.Length).ToArray()]};
        }
        Files.Json(Path.Combine(output,"distant-forest.json"),new{TreePlacements=trees.Length,InteriorPlacements=interior,FadingPlacements=selected,
            FadeStartMetres=600,FadeEndMetres=700,EligibleFraction=.25,CellSize,AllNearbyPlacementsRetained=true,ForestEdgesAndTallCrownsProtected=true});
    }
    internal static uint Hash(Vector3 position)
    {
        uint hash=2166136261;
        foreach(float coordinate in new[]{position.X,position.Y,position.Z})
        {uint value=unchecked((uint)(int)MathF.Round(coordinate*100));for(int i=0;i<4;i++){hash=unchecked((hash^(byte)value)*16777619);value>>=8;}}
        hash^=hash>>16;hash=unchecked(hash*0x7feb352d);hash^=hash>>15;hash=unchecked(hash*0x846ca68b);return hash^(hash>>16);
    }
}
