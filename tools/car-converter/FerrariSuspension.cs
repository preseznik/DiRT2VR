using System.Numerics;
using DiRT2VR.CarImport;
namespace DiRT2VR.CustomCars;

internal static class FerrariSuspension
{
    static string? Side(string name)=>name switch{"LF"=>"fl","RF"=>"fr","LR"=>"bl","RR"=>"br",_=>null};
    internal static string? Joint(string name)
    {
        if(name.StartsWith("SUSP_ARM_",StringComparison.Ordinal)){
            var token=name[9..];if(token.Length!=3)return null;var side=Side(token[..2]);if(side is null)return null;
            var member=token[2] switch{'1'=>"wishbone",'2'=>"suspension",'3'=>"suspension2",'4'=>"suspension3",_=>throw new InvalidDataException("Unsupported F2004 arm")};
            return member+"_"+side;
        }
        foreach(var prefix in new[]{"SUSP_","DISC_"})if(name.StartsWith(prefix,StringComparison.Ordinal)){
            var side=Side(name[prefix.Length..]);if(side is not null)return (prefix=="SUSP_"?"hub_caliper_":"disc_")+side;
        }
        return null;
    }
    internal static List<object> Bind(IReadOnlyList<FerrariExterior.Part> parts,Dictionary<string,Matrix4x4> pivots,Dictionary<string,Matrix4x4> fixedAnchors)
    {
        var reports=new List<object>();
        foreach(var side in new[]{"fl","fr","bl","br"}){
            if(!pivots.TryGetValue("wheel_"+side,out var wheel))continue;
            foreach(var name in new[]{"hub_caliper_","disc_"})if(pivots.ContainsKey(name+side))pivots[name+side]=Matrix4x4.CreateTranslation(wheel.Translation);
            foreach(var member in new[]{"wishbone","suspension","suspension2","suspension3"}){
                var key=member+"_"+side;if(!pivots.TryGetValue(key,out var root))continue;
                var meshes=parts.Where(p=>p.Joint==key).ToArray();if(meshes.Length==0)continue;
                // Supported-profile outboard endpoint from the source member's
                // actual world-space surface nearest the wheel centre.
                var points=meshes.SelectMany(p=>p.Mesh.Positions.Select(v=>Vector3.Transform(v,p.World))).ToArray();
                var closest=points.Min(v=>Vector3.DistanceSquared(v,wheel.Translation));
                var tip=points.Where(v=>Vector3.DistanceSquared(v,wheel.Translation)<=closest+.0001f).ToArray();
                var outer=tip.Aggregate(Vector3.Zero,(sum,v)=>sum+v)/tip.Length;var inner=root.Translation;
                if(!float.IsFinite(outer.X)||!float.IsFinite(outer.Y)||!float.IsFinite(outer.Z)||Vector3.Distance(inner,outer)<.1f)throw new InvalidDataException("Degenerate source suspension member: "+key);
                bool wheelParent=member=="suspension2";
                // Native PRC arm joints use identity rotation. Bake AC's bind
                // rotation into the vertices; retaining it on the animated joint
                // mixes AC local axes with EGO's linkage update axes.
                var bind=Matrix4x4.CreateTranslation(wheelParent?outer:inner);pivots[key]=bind;
                var fixedName=member switch{"suspension"=>"suspension_fixed_","suspension2"=>"suspension2_fixed_","suspension3"=>"suspension3_fixed_",_=>null};
                if(fixedName is not null)fixedAnchors[fixedName+side]=Matrix4x4.CreateTranslation(wheelParent?inner:outer);
                if(member=="suspension2"){
                    fixedAnchors["hub_caliper_fixed_"+side]=Matrix4x4.CreateTranslation(inner);
                    fixedAnchors["suspension6_fixed_"+side]=Matrix4x4.CreateTranslation(inner);
                }
                reports.Add(new{Joint=key,Meshes=meshes.Length,Vertices=points.Length,ChassisPoint=Array(inner),WheelPoint=Array(outer),NativeOrigin=Array(bind.Translation),WheelParent=wheelParent,EndpointConvention="Nearest source surface cluster; approximate, native runtime unaccepted"});
            }
        }
        return reports;
    }
    internal static (Dictionary<string,Matrix4x4> Pivots,Dictionary<string,Matrix4x4> Fixed) Reference(CarModel model)
    {
        var pivots=new Dictionary<string,Matrix4x4>();var parts=new List<FerrariExterior.Part>();
        void Visit(CarNode node,Matrix4x4 world,string joint,bool enabled){
            world=node.LocalTransform*world;enabled&=node.Active;
            var mapped=Joint(node.Name);if(mapped is not null){joint=mapped;pivots[joint]=world;}
            var wheel=node.Name switch{"WHEEL_LF"=>"wheel_fl","WHEEL_RF"=>"wheel_fr","WHEEL_LR"=>"wheel_bl","WHEEL_RR"=>"wheel_br",_=>null};
            if(wheel is not null)pivots[wheel]=world;
            if(joint!=""&&enabled&&node.Mesh is {} mesh&&mesh.Visible&&mesh.Renderable)parts.Add(new(mesh,world,joint));
            foreach(var child in node.Children)Visit(child,world,joint,enabled);
        }
        Visit(model.Root,Matrix4x4.Identity,"",true);var fixedAnchors=new Dictionary<string,Matrix4x4>();Bind(parts,pivots,fixedAnchors);return(pivots,fixedAnchors);
    }
    static float[] Array(Vector3 v)=>new[]{v.X,v.Y,v.Z};
}
