using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using EgoEngineLibrary.Vehicle;

namespace DiRT2VR.CustomCars;

internal static class FerrariHandling
{
    static float Number(string text)=>float.Parse(text,NumberStyles.Float,CultureInfo.InvariantCulture);
    internal static void Build(string donor,string schema,string source,string output)
    {
        donor=Path.GetFullPath(donor);source=Path.GetFullPath(source);output=Path.GetFullPath(output);
        if(Directory.Exists(output) || new[]{donor,source}.Any(p=>output==p || output.StartsWith(p+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)))throw new IOException("New output outside inputs required");
        using var extraction=JsonDocument.Parse(File.ReadAllText(Path.Combine(source,"extraction.json")));
        var hashes=extraction.RootElement.GetProperty("Files").EnumerateArray().ToDictionary(f=>f.GetProperty("Name").GetString()!,f=>f.GetProperty("Sha256").GetString()!,StringComparer.Ordinal);
        string Read(string name)
        {
            if(!hashes.TryGetValue(name,out var expected))throw new InvalidDataException("Unreceipted physics input");
            var bytes=File.ReadAllBytes(Path.Combine(source,name));
            if(!Convert.ToHexString(SHA256.HashData(bytes)).Equals(expected,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Source physics changed: "+name);
            return System.Text.Encoding.UTF8.GetString(bytes);
        }
        Dictionary<string,string> Ini(string name)
        {
            string section="";var values=new Dictionary<string,string>(StringComparer.Ordinal);
            foreach(var line in Read(name).Split('\n'))
            {
                var text=line.Split(';')[0].Trim();if(text.StartsWith('[') && text.EndsWith(']')){section=text[1..^1];continue;}
                int equals=text.IndexOf('=');if(equals>0)values.Add(section+"/"+text[..equals].Trim(),text[(equals+1)..].Trim());
            }
            return values;
        }
        var car=Ini("car.ini");var engine=Ini("engine.ini");var gears=Ini("drivetrain.ini");var brakes=Ini("brakes.ini");var tyres=Ini("tyres.ini");var suspension=Ini("suspensions.ini");
        var torque=Read("power.lut").Split('\n',StringSplitOptions.RemoveEmptyEntries).Where(l=>l.Contains('|')).Select(l=>l.Trim().Split('|')).Select(v=>(Rpm:Number(v[0]),Torque:Number(v[1]))).ToArray();
        if(torque.Length<3 || torque.Any(v=>!float.IsFinite(v.Rpm) || !float.IsFinite(v.Torque) || v.Torque<0) || torque.Zip(torque.Skip(1)).Any(v=>v.First.Rpm>=v.Second.Rpm))throw new InvalidDataException("Invalid torque curve");
        float Torque(float rpm)
        {
            if(rpm<=torque[0].Rpm)return torque[0].Torque;if(rpm>=torque[^1].Rpm)return torque[^1].Torque;
            int i=Array.FindIndex(torque,v=>v.Rpm>=rpm);var a=torque[i-1];var b=torque[i];return a.Torque+(b.Torque-a.Torque)*(rpm-a.Rpm)/(b.Rpm-a.Rpm);
        }
        float Angular(float rpm)=>rpm*MathF.PI/30;
        int count=(int)Number(gears["GEARS/COUNT"]);if(count!=7 || gears["TRACTION/TYPE"]!="RWD")throw new InvalidDataException("Supported F2004 drivetrain required");
        var limit=Angular(Number(engine["ENGINE_DATA/LIMITER"]));float step=limit/30;
        var power=new FloatList{count=32,step=step,items=Enumerable.Range(0,32).Select(i=>i>=30?0f:Torque(i*step*30/MathF.PI)*(i*step)).ToArray()};
        Directory.CreateDirectory(output);var files=new List<object>();var changes=new List<object>();
        foreach(var suffix in new[]{"","_A","_B"})
        {
            var donorPath=Path.Combine(donor,"cars/prc/prc"+suffix+".ctf");var page=new CtfEditorGamePage(File.OpenRead(schema),CtfEditorMainTabs.Other);
            var file=new CtfFile(donorPath,File.OpenRead(donorPath),page);
            void Set(string name,object value)
            {
                int id=Array.FindIndex(page.ctfEntryInfo,e=>e.name==name);if(id<0 || !file.entry.ContainsKey(id))throw new InvalidDataException("Missing native handling entry: "+name);
                if(suffix=="")changes.Add(new{Name=name,Original=file.entry[id],Converted=value});file.entry[id]=value;
            }
            Set("mass",Number(car["BASIC/TOTALMASS"]));Set("idle_rate",Angular(Number(engine["ENGINE_DATA/MINIMUM"])));Set("rev_limit",limit);
            Set("engine_inertia",Number(engine["ENGINE_DATA/INERTIA"]));Set("power",power);
            // Native inverse total ratio inferred from stock CTF speed/radius/revs.
            // The seven-speed and high-rev runtime proof must confirm this mapping.
            float final=Number(gears["GEARS/FINAL"]);
            string[] native={"gear_1st","gear_2nd","gear_3rd","gear_4th","gear_5th","gear_6th","gear_7th"};
            for(int i=0;i<count;i++)Set(native[i],1/(final*Number(gears["GEARS/GEAR_"+(i+1)])));
            Set("gear_8th",0f);Set("rear_drive_proportion",1f);
            // Fixed source profile: summed aero CdA=1.23913816 m^2 gives
            // approximately 0.759 kg/m at reference density. Native calibration
            // at 0.75 reaches all seven gears; the buggy donor's 4 held fourth
            // below its upshift RPM at about 105 mph.
            Set("air_resistance_z",0.75f);
            // Cosmetic acceleration lean is independent of the physical suspension.
            // Keep the accepted spring/ARB, inertia and camera tuning unchanged.
            Set("visual_acc_scale_roll",0f);
            Set("visual_acc_scale_pitch",0f);
            float steeringLock=Number(car["CONTROLS/STEER_LOCK"])/Number(car["CONTROLS/STEER_RATIO"]);
            Set("max_steering_lock",steeringLock);
            // Native pad steering reduces lock with speed, clamped to this floor.
            // Limit high-speed corrections while retaining full low-speed lock
            // and the speed threshold (4.2 degrees for the supported source).
            Set("pad_min_lock",steeringLock*0.35f);
            // Retain the stock STI curve selected in the previous gamepad tune.
            Set("pad_linearity",1f);
            // Native pad filtering limits wheel-axis change per second. The PRC's
            // 2/4 rates reach this car's small steering range almost immediately.
            // Build steering progressively, with quicker return toward centre.
            Set("low_speed_steering_rate",0.75f);
            Set("high_speed_steering_rate",0.5f);
            Set("steering_rate_to_centre_mult",2f);
            // Stock limits are sine-like visual wheel-axis caps, independently of physical steering lock.
            Set("visual_wheel_xv_limit",MathF.Sin(Number(car["CONTROLS/STEER_LOCK"])/Number(car["CONTROLS/STEER_RATIO"])*MathF.PI/180));
            Set("front_radius",Number(tyres["FRONT/RADIUS"]));Set("rear_radius",Number(tyres["REAR/RADIUS"]));
            Set("front_width",Number(tyres["FRONT/WIDTH"]));Set("rear_width",Number(tyres["REAR/WIDTH"]));
            Set("brake_torque_total",Number(brakes["DATA/MAX_TORQUE"]));Set("brake_bias",Number(brakes["DATA/FRONT_SHARE"]));
            // Retain the native donor handbrake for grid holds and the player binding.
            // Zeroing its strength makes both paths ineffective in the converted car.
            // The buggy's long clutch opening lets the low-inertia V10 flare
            // during a shift. Use the slower source shift time for this native
            // approximation; the native default delay is half the clutch time.
            float shiftTime=MathF.Max(Number(gears["GEARBOX/CHANGE_UP_TIME"]),Number(gears["GEARBOX/CHANGE_DN_TIME"]))/1000;
            Set("clutch_gear_time",shiftTime);
            Set("auto_clutch_gear_delay",shiftTime/2);
            Set("gear_change_throttle_off_time",Number(gears["GEARBOX/AUTO_CUTOFF_TIME"])/1000);
            Set("gear_change_throttle_on_time",Number(gears["GEARBOX/CHANGE_UP_TIME"])/1000);
            // These are a supported-profile approximation, not an equivalence of
            // AC's load-sensitive tyre and suspension simulation to the EGO model.
            int compound=(int)Number(tyres["COMPOUND_DEFAULT/INDEX"]);
            string Tyre(string axle,string key)=>tyres[axle+(compound==0?"":"_"+compound)+"/"+key];
            foreach(var (nativeAxle,sourceAxle) in new[]{("front","FRONT"),("rear","REAR")})
            {
                Set("grip_"+nativeAxle,(Number(Tyre(sourceAxle,"DX_REF"))+Number(Tyre(sourceAxle,"DY_REF")))*.5f);
                // Broaden the source slip window for native calibration after
                // reported abrupt breakaway. Scale both axles equally while
                // retaining peak grip.
                Set(nativeAxle+"_peak_slip",Number(Tyre(sourceAxle,"FRICTION_LIMIT_ANGLE"))*1.5f);
                Set("wheel_inertia_"+nativeAxle,Number(Tyre(sourceAxle,"ANGULAR_INERTIA")));
                Set("anti_roll_"+nativeAxle,Number(suspension["ARB/"+sourceAxle]));
                Set("suspension_strength_"+nativeAxle,Number(suspension[sourceAxle+"/SPRING_RATE"]));
                Set("damping_"+nativeAxle,Number(suspension[sourceAxle+"/DAMP_BUMP"]));
                Set(nativeAxle+"_fast_bump",Number(suspension[sourceAxle+"/DAMP_FAST_BUMP"]));
                Set(nativeAxle+"_slow_rebound",Number(suspension[sourceAxle+"/DAMP_REBOUND"]));
                Set(nativeAxle+"_fast_rebound",Number(suspension[sourceAxle+"/DAMP_FAST_REBOUND"]));
                Set("max_wheel_height_"+nativeAxle,Number(suspension[sourceAxle+"/BUMPSTOP_UP"]));
                Set("min_wheel_height_"+nativeAxle,-Number(suspension[sourceAxle+"/BUMPSTOP_DN"]));
                Set("visual_"+nativeAxle+"_max_height",Number(suspension[sourceAxle+"/BUMPSTOP_UP"]));
                Set("third_spring_str_"+nativeAxle,Number(suspension["HEAVE_"+sourceAxle+"/SPRING_RATE"]));
                Set("third_spring_dmp_"+nativeAxle,Number(suspension["HEAVE_"+sourceAxle+"/DAMP_BUMP"]));
            }
            var dimensions=car["BASIC/INERTIA"].Split(',').Select(Number).ToArray();
            if(dimensions.Length!=3)throw new InvalidDataException("Source inertia box dimensions required");
            float mass=Number(car["BASIC/TOTALMASS"]);
            Set("inertia_x",mass/12*(dimensions[1]*dimensions[1]+dimensions[2]*dimensions[2]));
            Set("inertia_y",mass/12*(dimensions[0]*dimensions[0]+dimensions[2]*dimensions[2]));
            Set("inertia_z",mass/12*(dimensions[0]*dimensions[0]+dimensions[1]*dimensions[1]));

            var path=Path.Combine(output,"f04"+suffix+".ctf");file.Write(path,File.Create(path));
            var original=File.ReadAllBytes(donorPath);if(original[^1]==0){using var append=new FileStream(path,FileMode.Append);append.WriteByte(0);}
            var check=new CtfFile(path,File.OpenRead(path),new CtfEditorGamePage(File.OpenRead(schema),CtfEditorMainTabs.Other));
            foreach(var entry in file.entry)if(!Equals(entry.Value,check.entry[entry.Key]))throw new InvalidDataException("CTF converted field readback failed");
            files.Add(new{Path=Path.GetFileName(path),Sha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))});
        }
        File.WriteAllText(Path.Combine(output,"handling-build.json"),JsonSerializer.Serialize(new{SourceAcdSha256=extraction.RootElement.GetProperty("SourceSha256").GetString(),SchemaSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(schema))),Files=files,Changes=changes,NativeRuntimeValidated=false,Limitations=new[]{"Seven gears observed with calibrated drag; manual gearbox, long-straight top speed and limiter acceptance remain pending","Power above source limiter is zeroed to approximate native fuel cut","Inverse gear-ratio mapping inferred; needs measured native speed/revs","Source suspension rates and default-medium tyre grip are native approximations; load sensitivity, thermal wear and geometry are unsupported","Native suspension and tyre units require runtime calibration","Source reference forward drag approximation; donor lateral drag, downforce and collision retained","Chassis inertia uses source dimensions as an equivalent uniform box","Brake torque native convention requires road test","Donor chassis dimensions remain until native collision/wheel configuration mapped"}},new JsonSerializerOptions{WriteIndented=true,IncludeFields=true}));
        Console.WriteLine("F2004 engine/gearing handling proof staged; native validation pending.");
    }
}
