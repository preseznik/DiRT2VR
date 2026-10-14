using System.Xml.Linq;
using DiRT2VR.Nordschleife;
using EgoEngineLibrary.Graphics.Pssg;
using EgoEngineLibrary.Graphics.Pssg.Elements;
using EgoEngineLibrary.Xml;

internal static class SpaMarkerVisibility
{
    internal static void Write(string game,string baseline,string output)
    {
        var objects=Files.Pssg(Files.Inside(baseline,"shared/objects.pssg"));
        using var input=File.OpenRead(Files.Inside(game,"tracks/london/battersea/route_1/ornament_attributes.xml"));
        var attributes=XDocument.Parse(new XmlFile(input).Document.OuterXml);
        var models=objects.Elements<PssgNode>().Where(n=>n.Name=="ROOTNODE"&&n.Id.StartsWith("spa_marker_",StringComparison.Ordinal)).ToArray();
        if(models.Length!=7)throw new InvalidDataException("Missing braking-board models");
        foreach(var root in models){
            string name=root.Id[..^5];
            var lod=root.ChildElements.OfType<PssgLodVisibleRenderNode>().Single();
            var instances=lod.ChildElements.OfType<PssgLodRenderInstances>().Single();
            if(instances.InstanceLists.Any())throw new InvalidDataException("Marker LODs already populated");
            var draws=lod.ChildElements.OfType<PssgRenderStreamInstance>().ToArray();
            if(draws.Length==0)throw new InvalidDataException("Marker has no source geometry");
            instances.LodCount=2;
            for(int level=0;level<2;level++){
                // These boards are already tiny meshes; retain the original faces
                // at every native distance tier instead of disappearing at a tier switch.
                var list=new XElement("LODRENDERINSTANCELIST",new XAttribute("lod",level==0?2f/3:1f/3));
                foreach(var draw in draws){var x=new XElement("root");draw.WriteXml(x);var d=x.Elements().Single();d.SetAttributeValue("id",draw.Id+"_distance_"+level);list.Add(d);}
                var doc=new XDocument(new XElement("PSSGFILE",new XElement("PSSGDATABASE",new XElement("LIBRARY",new XAttribute("type","NODE"),new XElement("ROOTNODE",new XAttribute("id","temporary"),new XElement("LODVISIBLERENDERNODE",new XAttribute("id","temporary_lod"),new XElement("LODRENDERINSTANCES",new XAttribute("lodCount",1),list)))))));
                var fragment=Files.FromDocument(doc);instances.AppendChild(fragment.Elements<PssgLodRenderInstanceList>().Single().DeepClone());
            }
            if(attributes.Root!.Elements().Any(e=>(string?)e.Attribute("name")==name))throw new InvalidDataException("Marker distance entry already exists");
            attributes.Root.Add(new XElement("ornament_attributes",new XAttribute("name",name),new XAttribute("lod_distance_00","100.00"),new XAttribute("lod_distance_01","220.00"),new XAttribute("lod_distance_02","400.00"),new XAttribute("key_feature","1")));
        }
        Files.Save(objects,Path.Combine(output,"shared/objects.pssg"));
        Files.Xml(attributes,Path.Combine(output,"ornament_attributes.xml"));
        var saved=Files.Pssg(Path.Combine(output,"shared/objects.pssg"));
        foreach(var lod in saved.Elements<PssgLodVisibleRenderNode>().Where(n=>n.Id.StartsWith("spa_marker_",StringComparison.Ordinal))){var lists=lod.ChildElements.OfType<PssgLodRenderInstances>().Single();if(lists.LodCount!=2||lists.InstanceLists.Count()!=2||lists.InstanceLists.Any(l=>!l.ChildElements.OfType<PssgRenderStreamInstance>().Any()))throw new InvalidDataException("Marker distance draw readback");}
        var before=Files.Pssg(Files.Inside(baseline,"shared/objects.pssg"));var after=saved.Elements<PssgObject>().ToDictionary(e=>e.Id);
        XElement Xml(PssgElement e){var root=new XElement("root");e.WriteXml(root);return root.Elements().Single();}
        foreach(var e in before.Elements<PssgObject>()){
            if(e is PssgNode node){var next=(PssgNode)after[e.Id];if(node.Transform.Transform!=next.Transform.Transform||node.BoundingBox.BoundsMin!=next.BoundingBox.BoundsMin||node.BoundingBox.BoundsMax!=next.BoundingBox.BoundsMax)throw new InvalidDataException("Board node geometry changed");}
            else if(!XNode.DeepEquals(Xml(e),Xml(after[e.Id])))throw new InvalidDataException("Original object resource changed: "+e.Id);
        }
        Files.Json(Path.Combine(output,"marker-visibility.json"),new{Passed=true,Models=7,Placements=24,PreviousExplicitDistanceEntries=0,PreviousDistanceLists=0,DistanceListsPerModel=2,DistanceMetres=new[]{100,220,400},SourceGeometryTexturesAndRigidBehaviorUnchanged=true,NativeRuntimeValidated=false,Diagnosis="Missing explicit ornament distances and empty LOD chains corrected; native visibility still requires player testing"});
    }
}
