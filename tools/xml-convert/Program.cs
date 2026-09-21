using System;
using System.IO;
using EgoEngineLibrary.Xml;

if(args.Length!=3 || (args[0]!="decode" && args[0]!="encode")) {
    Console.Error.WriteLine("Usage: xml-convert decode|encode input output"); return 2;
}
if(Path.GetFullPath(args[1])==Path.GetFullPath(args[2])) throw new ArgumentException("Input and output must differ");
using var input=File.OpenRead(args[1]);
var xml=new XmlFile(input);
using var output=new FileStream(args[2],FileMode.CreateNew);
xml.Write(output,args[0]=="decode" ? XmlType.Text : XmlType.BinXml);
return 0;
