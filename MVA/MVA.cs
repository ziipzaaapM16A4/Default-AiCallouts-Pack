using System;
using System.Collections.Generic;
using System.Linq;
using Rage;
using System.Reflection;
using LSPD_First_Response.Mod.API;
using LSPDFR_Functions = LSPD_First_Response.Mod.API.Functions;
using AmbientAICallouts.API;
using Functions = AmbientAICallouts.API.Functions;

namespace MVA
{
    public class MVA : AiCallout
    {
        internal readonly List<Model> carModelList = new List<Model> { "SEMINOLE", "SCHAFTER2", "PRIMO", "BALLER2", "MINIVAN", "EXEMPLAR", "TAXI", "WARRENER", "ORACLE", "HABANERO","SABREGT"};
        private Rage.Object notepad = null;
        private float heading;
        private bool finished = false;
        private bool senarioTaskAsigned;
        private static Random randomizer = new Random();
        private bool isSTPRunning;

        public override bool Setup()
        {
            try
            {
                SceneInfo = "Motor Vehicle Accident";
                CalloutDetailsString = "MOTOR_VEHICLE_ACCIDENT";
                Vector3 roadside = World.GetNextPositionOnStreet(Game.LocalPlayer.Character.Position.Around(AmbientAICallouts.API.Functions.minimumAiCalloutDistance + 10f, AmbientAICallouts.API.Functions.maximumAiCalloutDistance - 10f));
                bool posFound = false;
                int trys = 0;
                while (!posFound && trys < 30)
                {
                    roadside = World.GetNextPositionOnStreet(Game.LocalPlayer.Character.Position.Around(AmbientAICallouts.API.Functions.minimumAiCalloutDistance + 10f, AmbientAICallouts.API.Functions.maximumAiCalloutDistance - 10f));


                    //isSTPRunning = IsExternalPluginRunning("StopThePed", new Version("4.9.3.5"));
                    //Game.LogTrivial("[AmbientAICallouts] [initialization] INFO: Detection - StopThePed: " + isSTPRunning);

                    Vector3 irrelevant;
                    heading = Unit.Heading;       //vieleicht guckt der MVA dann in fahrtrichtung der unit

                    Rage.Native.NativeFunction.Natives.x240A18690AE96513<bool>(roadside.X, roadside.Y, roadside.Z, out roadside, 0, 3.0f, 0f);//GET_CLOSEST_VEHICLE_NODE

                    Rage.Native.NativeFunction.Natives.xA0F8A7517A273C05<bool>(roadside.X, roadside.Y, roadside.Z, heading, out roadside); //_GET_ROAD_SIDE_POINT_WITH_HEADING
                    Rage.Native.NativeFunction.Natives.xFF071FB798B803B0<bool>(roadside.X, roadside.Y, roadside.Z, out irrelevant, out heading, 0, 3.0f, 0f); //GET_CLOSEST_VEHICLE_NODE_WITH_HEADING //Find Side of the road.

                    Location = roadside;


                    if (Location.DistanceTo(Game.LocalPlayer.Character.Position) > AmbientAICallouts.API.Functions.minimumAiCalloutDistance
                     && Location.DistanceTo(Game.LocalPlayer.Character.Position) < AmbientAICallouts.API.Functions.maximumAiCalloutDistance)
                        posFound = true;
                    trys++;
                }

                //spawn 2 vehicles at the side of the road
                AmbientAICallouts.API.Functions.CleanArea(roadside, 25f);
                SuspectsVehicles.Add(new Vehicle(carModelList[randomizer.Next(0, carModelList.Count)], roadside, heading) { IsEngineOn = true, IndicatorLightsStatus = VehicleIndicatorLightsStatus.Both });
                SuspectsVehicles.Add(new Vehicle(carModelList[randomizer.Next(0, carModelList.Count)], SuspectsVehicles[0].GetOffsetPositionFront(-6f), heading) { IsEngineOn = true, IndicatorLightsStatus = VehicleIndicatorLightsStatus.Both });
                DeformBack(SuspectsVehicles[0]);
                DeformFront(SuspectsVehicles[1]);

                LogTrivialDebug_withAiC($"DEBUG: Create Driver and make them go to the side of the road");
                Suspects.Add(SuspectsVehicles[0].CreateRandomDriver());
                Suspects.Add(SuspectsVehicles[1].CreateRandomDriver());
                foreach (var suspect in Suspects) suspect.Tasks.LeaveVehicle(LeaveVehicleFlags.None);
                Suspects[0].Tasks.FollowNavigationMeshToPosition(SuspectsVehicles[0].RightPosition, heading + 180f, 1f, 2f, 9000);
                Suspects[1].Tasks.FollowNavigationMeshToPosition(SuspectsVehicles[1].RightPosition, heading + 0f, 1f, 2f, 9000);
                GameFiber.Sleep(2000);

                senarioTaskAsigned = false;
                //Suspect Animiation Fiber
                GameFiber.StartNew(delegate
                {
                    try
                    {
                        List<string> idleAnims = new List<string>() { "idle_a", "idle_b", "idle_c" };
                        while (!finished)
                        {                                                                                   //sollange call läuft //Workaround
                            while (!senarioTaskAsigned)
                            {
                                //try {
                                for (int i = 0; i < Suspects.Count; i++)
                                    if (Suspects[i] 
                                    && !senarioTaskAsigned 
                                    && !finished
                                    && !LSPDFR_Functions.IsPedArrested(Suspects[i]) 
                                    && !LSPDFR_Functions.IsPedBeingCuffed(Suspects[i]) 
                                    && !LSPDFR_Functions.IsPedBeingFrisked(Suspects[i]) 
                                    && !LSPDFR_Functions.IsPedBeingGrabbed(Suspects[i]) 
                                    && !LSPDFR_Functions.IsPedInPursuit(Suspects[i])
                                    )
                                    {
                                        if (Suspects[i].Tasks.CurrentTaskStatus == Rage.TaskStatus.NoTask)
                                        {
                                            Suspects[i].Tasks.PlayAnimation(new AnimationDictionary("oddjobs@towingangryidle_a"), idleAnims[randomizer.Next(0, idleAnims.Count)], 1f, AnimationFlags.None);
                                            GameFiber.Sleep(3400);
                                        }
                                    }
                                //} catch (Exception e) { Game.LogTrivialDebug($"[AmbientAICallouts] [Fiber {fiberNumber}]  WARNING: Animation failed: " + e); }
                                int a = 0;
                                while (a++ < 18 && !senarioTaskAsigned) { GameFiber.Sleep(500); }

                            }
                            GameFiber.Sleep(2000);
                        }
                    }
                    catch (System.Threading.ThreadAbortException) {}
                    catch (Exception e) { LogTrivialDebug_withAiC($"ERROR: in Animation maker Fiber: {e}"); }
                }, $"[AmbientAICallouts] [AiCallout MVA] Animation maker Fiber");

                return true;
            }
            catch (System.Threading.ThreadAbortException) { return false; }
            catch (Exception e)
            {
                LogTrivial_withAiC("ERROR: in AICallout object: " + e);
                return false;
            }
        }
        public override bool Process()
        {
            //Code for processing the the crime scene. return true when Succesfull.
            //Example: Cops arrive; Getting out; Starring at suspects; End();
            try
            {
                LogTrivialDebug_withAiC($"DEBUG: Waiting for Cops to Arrive");
                if (!IsUnitInTime(100f, 130))  //if vehicle is never reaching its location
                {
                    Disregard();
                }
                else  //if vehicle is reaching its location
                {
                    //Waiting until the unit arrives
                    GameFiber.WaitWhile(() => Unit.Position.DistanceTo(Location) >= 65f, 25000);
                    Unit.IsSirenSilent = true;
                    Unit.TopSpeed = 16f;
                    OfficerReportOnScene();

                    GameFiber.WaitWhile(() => Unit.Position.DistanceTo(Location) >= 45f, 20000);
                    var unitTask = UnitOfficers[0].Tasks.ParkVehicle(SuspectsVehicles[1].GetOffsetPositionFront(-9f), heading);

                    GameFiber.WaitWhile(() => unitTask.IsActive, 10000);
                    if (Unit.Position.DistanceTo(SuspectsVehicles[1].GetOffsetPositionFront(-9f)) >= 4f) Unit.Position = SuspectsVehicles[1].GetOffsetPositionFront(-9f); Unit.Heading = heading;
                    OfficersLeaveVehicle(false);
                    var cone0 = new Rage.Object(new Model("prop_mp_cone_02"), Unit.GetOffsetPosition(new Vector3(-0.7f, -4f, 0f)), Unit.Heading); cone0.Position = cone0.GetOffsetPositionUp(-cone0.HeightAboveGround); cone0.IsPersistent = false;
                    var cone1 = new Rage.Object(new Model("prop_mp_cone_02"), Unit.GetOffsetPosition(new Vector3(0.0f, -5f, 0f)), Unit.Heading); cone1.Position = cone1.GetOffsetPositionUp(-cone1.HeightAboveGround); cone1.IsPersistent = false;
                    var cone2 = new Rage.Object(new Model("prop_mp_cone_02"), Unit.GetOffsetPosition(new Vector3(+0.7f, -6f, 0f)), Unit.Heading); cone2.Position = cone2.GetOffsetPositionUp(-cone2.HeightAboveGround); cone2.IsPersistent = false;

                    //Aproach the Suspects
                    LogTrivialDebug_withAiC($"DEBUG: Aproach");
                    for (int i = 0; i < UnitOfficers.Count; i++) { UnitOfficers[i].Tasks.FollowNavigationMeshToPosition(SuspectsVehicles[1].GetOffsetPosition(new Vector3(2.5f + i, -2f, 0f)), heading + 0f, 1f, 1f, 20000); }     //ich versuche hier so ein wenig abstand zwichen allen peds zu erstellen
                    GameFiber.WaitWhile(() => UnitOfficers[0].Position.DistanceTo(SuspectsVehicles[1].RightPosition) > 8f, 25000);
                    //Suspects are getting in position
                    for (int i = 0; i < Suspects.Count; i++) { Suspects[i].Tasks.FollowNavigationMeshToPosition(SuspectsVehicles[1].GetOffsetPosition(new Vector3(2.5f + i, 0f, 0f)), heading + 180f, 1f, 1f, 20000); }    //ich versuche hier so ein wenig abstand zwichen allen peds zu erstellen
                    GameFiber.WaitUntil(() => UnitOfficers[0].Tasks.CurrentTaskStatus == Rage.TaskStatus.NoTask, 15000);

                    // Wenn Cops nicht zeitig neber die autos kommen
                    for (int i = 0; i < UnitOfficers.Count; i++)
                        if (UnitOfficers[i].Position.DistanceTo(SuspectsVehicles[1].GetOffsetPosition(new Vector3(2f + i, 0f, 0f))) >= 3f)
                        {
                            UnitOfficers[i].Position = SuspectsVehicles[1].GetOffsetPosition(new Vector3(2f + i, 0f, 0f));
                            Rage.Native.NativeFunction.Natives.x5AD23D40115353AC(UnitOfficers[i], Suspects[i], 0);      //TASK_TURN_PED_TO_FACE_ENTITY
                        }

                    //Talk To The Suspects
                    LogTrivialDebug_withAiC($"DEBUG: Peds are Talking");
                    for (int i = 1; i < UnitOfficers.Count; i++) { UnitOfficers[i].Tasks.PlayAnimation(new AnimationDictionary("amb@code_human_wander_idles_cop@male@static"), "static", 1f, AnimationFlags.Loop); }

                    if (playerRespondingInAdditon)
                    {
                        Suspects[1].Tasks.FollowNavigationMeshToPosition(SuspectsVehicles[0].RightPosition, SuspectsVehicles[0].Heading - 90f, 1f, 2f, 9000);
                        Rage.Native.NativeFunction.Natives.x5AD23D40115353AC(UnitOfficers[0], Suspects[0], 0);      //TASK_TURN_PED_TO_FACE_ENTITY
                        Rage.Native.NativeFunction.Natives.x5AD23D40115353AC(UnitOfficers[1], Suspects[0], 0);      //TASK_TURN_PED_TO_FACE_ENTITY
                        for (int i = 1; i < UnitOfficers.Count; i++) { UnitOfficers[i].Tasks.PlayAnimation(new AnimationDictionary("amb@code_human_wander_idles_cop@male@static"), "static", 1f, AnimationFlags.Loop); }
                        
                        if (Suspects.Count == 1)
                        {
                            TaskJustGiveCover();
                        }
                        else
                        {
                            TaskIdentifySecondPed();
                        }

                        if (cone0) cone0.Delete();
                        if (cone1) cone1.Delete();
                        if (cone2) cone2.Delete();
                    } else
                    {
                        if (IsAiTakingCare()) //checkforSelfhandle
                        {
                            LogTrivial_withAiC($"INFO: chose selfhandle path");
                            //Officer 0 Notebook animation Fiber
                            var notebookAnimationFinished = false;
                            GameFiber.StartNew(delegate
                            {
                                try
                                {
                                    notepad = new Rage.Object("prop_notepad_02", UnitOfficers[0].Position, 0f);
                                    notepad.AttachTo(UnitOfficers[0], Rage.Native.NativeFunction.Natives.GET_PED_BONE_INDEX<int>(UnitOfficers[0], 18905), new Vector3(0.16f, 0.05f, -0.01f), new Rotator(-37f, -19f, .32f));

                                    var taskPullsOutNotebook = UnitOfficers[0].Tasks.PlayAnimation(new AnimationDictionary("amb@medic@standing@timeofdeath@enter"), "enter", 2f, AnimationFlags.None);
                                    GameFiber.SleepUntil(() => taskPullsOutNotebook.CurrentTimeRatio > 0.92f, 10000);
                                    UnitOfficers[0].Tasks.PlayAnimation(new AnimationDictionary("amb@medic@standing@timeofdeath@base"), "base", 2f, AnimationFlags.Loop);
                                    GameFiber.Sleep(4000);

                                    var watchClock = UnitOfficers[0].Tasks.PlayAnimation(new AnimationDictionary("amb@medic@standing@timeofdeath@idle_a"), "idle_b", 2f, AnimationFlags.None);
                                    GameFiber.SleepUntil(() => watchClock.CurrentTimeRatio > 0.92f, 10000);

                                    UnitOfficers[0].Tasks.PlayAnimation(new AnimationDictionary("amb@medic@standing@timeofdeath@base"), "base", 2f, AnimationFlags.Loop);
                                    GameFiber.Sleep(2500);
                                    if (UnitOfficers.Count != 1) UnitOfficers[1].PlayAmbientSpeech(null, "SETTLE_DOWN", 0, SpeechModifier.Force);
                                    GameFiber.Sleep(200);

                                    var looksAround = UnitOfficers[0].Tasks.PlayAnimation(new AnimationDictionary("amb@medic@standing@timeofdeath@idle_a"), "idle_c", 2f, AnimationFlags.None);
                                    GameFiber.SleepUntil(() => looksAround.CurrentTimeRatio > 0.92f, 10000);

                                    UnitOfficers[0].Tasks.PlayAnimation(new AnimationDictionary("amb@medic@standing@timeofdeath@base"), "base", 2f, AnimationFlags.Loop);
                                    GameFiber.Sleep(4000);

                                    var putNotebookBack = UnitOfficers[0].Tasks.PlayAnimation(new AnimationDictionary("amb@medic@standing@timeofdeath@exit"), "exit", 2f, AnimationFlags.None);
                                    GameFiber.SleepUntil(() => !putNotebookBack.IsActive, 10000);
                                    if (notepad) notepad.Delete();
                                    UnitOfficers[0].Tasks.Clear();
                                    notebookAnimationFinished = true;
                                }
                                catch (System.Threading.ThreadAbortException) { }
                                catch (Exception e) { LogTrivialDebug_withAiC($"ERROR: in Animation maker Fiber: {e}"); }
                            }, $"[AmbientAICallouts] [AiCallout MVA] Animation maker Fiber");

                            GameFiber.SleepUntil(() => notebookAnimationFinished, 50000);
                            senarioTaskAsigned = true;
                            GameFiber.Sleep(2000);

                            //Peds Leave, Cops Aproach own vehicle
                            Game.LogTrivialDebug($"[AmbientAICallouts] [AiCallout MVA] DEBUG: Scene cleared");
                            AiCandHA_DismissHelicopter();
                            foreach (var suspect in Suspects) if (suspect) { suspect.Tasks.Clear(); suspect.Dismiss(); GameFiber.Sleep(6000); }
                            for (int i = 1; i < UnitOfficers.Count; i++) { UnitOfficers[i].Tasks.Clear(); }
                            if (UnitOfficers.Count != 1)
                            {
                                var officerAtCopCar = UnitOfficers[0].Tasks.FollowNavigationMeshToPosition(Unit.GetOffsetPosition(new Vector3(2.7f, 0f, 0f)), heading + 0f, 0.75f);
                                UnitOfficers[1].Tasks.FollowNavigationMeshToPosition(Unit.GetOffsetPosition(new Vector3(2.2f, 1.3f, 0f)), heading + 180f, 0.75f).WaitForCompletion();
                                while (officerAtCopCar.IsActive) { GameFiber.Sleep(300); }
                                Rage.Native.NativeFunction.Natives.x5AD23D40115353AC(UnitOfficers[0], UnitOfficers[1], 0);      //TASK_TURN_PED_TO_FACE_ENTITY
                                Rage.Native.NativeFunction.Natives.x5AD23D40115353AC(UnitOfficers[1], UnitOfficers[0], 0);      //TASK_TURN_PED_TO_FACE_ENTITY
                                GameFiber.Sleep(16000);
                            }
                            if (cone0) cone0.Delete();
                            if (cone1) cone1.Delete();
                            if (cone2) cone2.Delete();
                            finished = true;
                            EnterAndDismiss();

                        }
                        else //Callout suspects are getting agressive 
                        {
                            LogTrivial_withAiC($"INFO: choosed callout path");
                            bool OffiverRequiringAssistanceUSED = false;

                            var taskPullsOutNotebook = UnitOfficers[0].Tasks.PlayAnimation(new AnimationDictionary("amb@medic@standing@timeofdeath@enter"), "enter", 2f, AnimationFlags.None);
                            GameFiber.SleepUntil(() => taskPullsOutNotebook.CurrentTimeRatio > 0.92f, 10000);
                            UnitOfficers[0].Tasks.PlayAnimation(new AnimationDictionary("amb@medic@standing@timeofdeath@base"), "base", 2f, AnimationFlags.Loop);

                            GameFiber.Sleep(15000);
                            UnitOfficers[0].Tasks.Clear();
                            senarioTaskAsigned = false;
                            finished = true;
                            switch (randomizer.Next(0, 6))                                                                                                           //FOR VIDEO EDITING
                            {
                                case 0:
                                    UnitCallsForBackup("AAIC-OfficerDown");
                                    break;
                                case 1:
                                    UnitCallsForBackup("AAIC-OfficerInPursuit");
                                    break;
                                case 2:
                                    UnitCallsForBackup("AAIC-OfficerUnderFire");
                                    break;
                                default:
                                    //Suspect leaves this discussion between suspect0 and the cops.   ++++++++   Cops turns to suspect 0;
                                    UnitOfficers[0].PlayAmbientSpeech(null, "SETTLE_DOWN", 0, SpeechModifier.Force);
                                    GameFiber.Sleep(2000);
                                    Suspects[1].PlayAmbientSpeech(null, "GENERIC_WHATEVER", 0, SpeechModifier.Force);
                                    GameFiber.Sleep(1000);
                                    Suspects[1].Tasks.FollowNavigationMeshToPosition(SuspectsVehicles[0].RightPosition, SuspectsVehicles[0].Heading - 90f, 1f, 2f, 9000);
                                    Rage.Native.NativeFunction.Natives.x5AD23D40115353AC(UnitOfficers[0], Suspects[0], 0);      //TASK_TURN_PED_TO_FACE_ENTITY
                                    Rage.Native.NativeFunction.Natives.x5AD23D40115353AC(UnitOfficers[1], Suspects[0], 0);      //TASK_TURN_PED_TO_FACE_ENTITY
                                    for (int i = 1; i < UnitOfficers.Count; i++) { UnitOfficers[i].Tasks.PlayAnimation(new AnimationDictionary("amb@code_human_wander_idles_cop@male@static"), "static", 1f, AnimationFlags.Loop); }

                                    UnitCallsForBackup("AAIC-OfficerRequiringAssistance");
                                    OffiverRequiringAssistanceUSED = true;
                                    break;
                            }


                            if (OffiverRequiringAssistanceUSED)
                            {
                                GameFiber.StartNew(delegate
                                {
                                    try
                                    {
                                        notepad = new Rage.Object("prop_notepad_02", UnitOfficers[0].Position, 0f);
                                        notepad.AttachTo(UnitOfficers[0], Rage.Native.NativeFunction.Natives.GET_PED_BONE_INDEX<int>(UnitOfficers[0], 18905), new Vector3(0.16f, 0.05f, -0.01f), new Rotator(-37f, -19f, .32f));

                                        if (UnitOfficers[0])
                                            if (LSPDFR_Functions.IsCopBusy(UnitOfficers[0], false))
                                            {
                                                GameFiber.Sleep(4000);
                                            }

                                        if (UnitOfficers[0])
                                            if (LSPDFR_Functions.IsCopBusy(UnitOfficers[0], false))
                                            {
                                                var watchClock = UnitOfficers[0].Tasks.PlayAnimation(new AnimationDictionary("amb@medic@standing@timeofdeath@idle_a"), "idle_b", 2f, AnimationFlags.None);
                                                GameFiber.SleepUntil(() => watchClock.CurrentTimeRatio > 0.92f, 10000);
                                            }

                                        if (UnitOfficers[0])
                                            if (LSPDFR_Functions.IsCopBusy(UnitOfficers[0], false))
                                            {
                                                UnitOfficers[0].Tasks.PlayAnimation(new AnimationDictionary("amb@medic@standing@timeofdeath@base"), "base", 2f, AnimationFlags.Loop);
                                                GameFiber.Sleep(2500);
                                                if (UnitOfficers.Count != 1) UnitOfficers[1].PlayAmbientSpeech(null, "SETTLE_DOWN", 0, SpeechModifier.Force);
                                                GameFiber.Sleep(200);
                                            }

                                        if (UnitOfficers[0])
                                            if (LSPDFR_Functions.IsCopBusy(UnitOfficers[0], false))
                                            {
                                                var looksAround = UnitOfficers[0].Tasks.PlayAnimation(new AnimationDictionary("amb@medic@standing@timeofdeath@idle_a"), "idle_c", 2f, AnimationFlags.None);
                                                GameFiber.SleepUntil(() => looksAround.CurrentTimeRatio > 0.92f, 10000);
                                            }

                                        if (UnitOfficers[0])
                                            if (LSPDFR_Functions.IsCopBusy(UnitOfficers[0], false))
                                            {
                                                UnitOfficers[0].Tasks.PlayAnimation(new AnimationDictionary("amb@medic@standing@timeofdeath@base"), "base", 2f, AnimationFlags.Loop);
                                                GameFiber.Sleep(31000);
                                            }
                                        if (notepad) notepad.Delete();
                                    }
                                    catch (System.Threading.ThreadAbortException) { }
                                    catch (Exception e) { LogTrivialDebug_withAiC($"ERROR: in Animation maker Fiber: {e}"); }
                                }, $"[AmbientAICallouts] [AiCallout MVA] Animation maker Fiber");
                            }


                            GameFiber.Sleep(15000);
                            while (LSPDFR_Functions.IsCalloutRunning()) { GameFiber.Sleep(11000); }    //OLD:OfficerRequiringAssistance.finished or OfficerInPursuit.finished

                        }
                    }
                }

                return true;
            }
            catch (System.Threading.ThreadAbortException) { return false; }
            catch (Exception e)
            {
                LogTrivial_withAiC("ERROR: in AICallout object: " + e);
                return false;
            }
        }
        public override bool End()
        {
            //Code for finishing the the crime scene. return true when Succesfull.
            //Example: Cops getting back into their vehicle. drive away dismiss the rest. after 90 secconds delete if possible entitys that have not moved away.
            try
            {
                var suspectsRecoveryVar = Suspects;                    //muss ich kopieren da Suspects; durch den async aufruf nach 61 sekunden wahrscheinlich nichtmehr existiert.
                var suspectsVehicleRecoveryVar = SuspectsVehicles;
                var locationRecoveryVar = Location;
                GameFiber.StartNew(delegate
                {
                    GameFiber.Sleep(61000);
                    LogTrivial_withAiC("INFO: starting now the check for Entitys which has not been cleaned up. Deleting if not");
                    if (suspectsRecoveryVar.Any()) { 
                        foreach (var suspect in suspectsRecoveryVar) { 
                            if (suspect) 
                                if (suspect.Position.DistanceTo(locationRecoveryVar) < 9f) 
                                    try { suspect.Delete(); 
                                    } catch { } 
                        } 
                    }
                    if (suspectsVehicleRecoveryVar.Any()) { 
                        foreach (var vehicle in suspectsVehicleRecoveryVar) { 
                            if (vehicle) 
                                if (vehicle.Position.DistanceTo(locationRecoveryVar) < 9f || vehicle.IsEmpty) 
                                    try { vehicle.Delete(); 
                                    } catch { } 
                        } 
                    }
                    if (Unit) 
                        if (Unit.DistanceTo(Location) < 9f) { 
                            Unit.Delete(); 
                            foreach (var officer in UnitOfficers) { 
                                if (officer) officer.Delete();
                            } 
                        }
                });
                return true;
            }
            catch (System.Threading.ThreadAbortException) { return false; }
            catch (Exception e)
            {
                LogTrivial_withAiC("ERROR: in AICallout object: " + e);
                return false;
            }
        }

        private static void DeformBack(Vehicle vehicleToDamage)
        {
            Vehicle vehicle = vehicleToDamage;
            vehicle.Repair();
            GameFiber.Yield();
            vehicle.Deform(new Vector3(-0.05292153f, -2.320627f, 0.4511213f), 50f, 100f);
            vehicle.Deform(new Vector3(-0.05968011f, -2.313982f, 0.4096715f), 50f, 100f);
            vehicle.Deform(new Vector3(-0.09274966f, -2.309759f, 0.3835794f), 50f, 100f);
            vehicle.Deform(new Vector3(-0.1282739f, -2.309116f, 0.3797662f), 50f, 100f);
            vehicle.Deform(new Vector3(-0.1895543f, -2.323293f, 0.4677997f), 50f, 100f);
            vehicle.Deform(new Vector3(0.6040038f, -1.45219f, -0.01871214f), 50f, 100f);
            GameFiber.Yield();
            vehicle.Deform(new Vector3(0.7410691f, -1.298735f, 0.08656909f), 50f, 100f);
            vehicle.Deform(new Vector3(0.8491221f, -1.219222f, 0.1349207f), 50f, 100f);
            vehicle.Deform(new Vector3(0.9219564f, -1.219252f, 0.1797397f), 50f, 100f);
            vehicle.Deform(new Vector3(0.9187003f, -1.219252f, 0.3084904f), 50f, 100f);
            vehicle.Deform(new Vector3(0.8612307f, -1.219252f, 0.4511593f), 50f, 100f);
            GameFiber.Yield();
            vehicle.Deform(new Vector3(0.8310509f, -1.150333f, 0.5130477f), 50f, 100f);
            vehicle.Deform(new Vector3(0.8311118f, -1.075899f, 0.5244957f), 50f, 100f);
            vehicle.Deform(new Vector3(0.8311118f, -1.068828f, 0.5285313f), 50f, 100f);
            vehicle.Deform(new Vector3(0.8310509f, -1.052877f, 0.52509f), 50f, 100f);
            vehicle.Deform(new Vector3(0.8311117f, -0.9853669f, 0.4418441f), 50f, 100f);
            GameFiber.Yield();
            vehicle.Deform(new Vector3(0.8309898f, -0.8736728f, 0.3084125f), 50f, 100f);
            vehicle.Deform(new Vector3(0.8309898f, -0.8353991f, 0.1865784f), 50f, 100f);
            vehicle.Deform(new Vector3(0.8309287f, -0.8560035f, 0.05632787f), 50f, 100f);
            vehicle.Deform(new Vector3(0.8308676f, -0.9379998f, -0.07993016f), 50f, 100f);
            vehicle.Deform(new Vector3(0.6004395f, -1.316034f, -0.03951463f), 50f, 100f);
            GameFiber.Yield();
            vehicle.Deform(new Vector3(-0.2411834f, -2.346989f, 0.615545f), 50f, 100f);
            vehicle.Deform(new Vector3(-0.1030751f, -2.323719f, 0.4704506f), 50f, 100f);
            vehicle.Deform(new Vector3(0.09219498f, -2.294143f, 0.2864112f), 50f, 100f);
            vehicle.Deform(new Vector3(0.1888167f, -2.313434f, 0.197658f), 50f, 100f);
            vehicle.Deform(new Vector3(0.2012938f, -2.32011f, 0.1826379f), 50f, 100f);
            GameFiber.Yield();
            vehicle.Deform(new Vector3(0.1985288f, -2.322218f, 0.1744757f), 50f, 100f);
            vehicle.Deform(new Vector3(0.1934866f, -2.320688f, 0.1767981f), 50f, 100f);
            vehicle.Deform(new Vector3(0.1157355f, -2.310555f, 0.1702991f), 50f, 100f);
            vehicle.Deform(new Vector3(-0.08027236f, -2.291451f, 0.2540919f), 50f, 100f);
            vehicle.Deform(new Vector3(-0.2300593f, -2.311321f, 0.3205611f), 50f, 100f);
            GameFiber.Yield();
            vehicle.Deform(new Vector3(-0.2656468f, -2.316891f, 0.3159897f), 50f, 100f);
            vehicle.Deform(new Vector3(-0.2831027f, -2.320384f, 0.2944447f), 50f, 100f);
            vehicle.Deform(new Vector3(-0.3197968f, -2.313921f, 0.2603326f), 50f, 100f);
            vehicle.Deform(new Vector3(-0.3269883f, -2.316374f, 0.2208233f), 50f, 100f);
            vehicle.Deform(new Vector3(-0.3275404f, -2.328742f, 0.1813504f), 50f, 100f);
            GameFiber.Yield();
            vehicle.Deform(new Vector3(-0.2733903f, -2.351764f, 0.09178628f), 50f, 100f);
            vehicle.Deform(new Vector3(-0.1463453f, -2.365546f, -0.008995992f), 50f, 100f);
            vehicle.Deform(new Vector3(-0.09508513f, -2.365607f, -0.03464917f), 50f, 100f);
            vehicle.Deform(new Vector3(-0.06011258f, -2.353786f, -0.02019344f), 50f, 100f);
            vehicle.Deform(new Vector3(-0.05304356f, -2.338509f, 0.01792969f), 50f, 100f);
            GameFiber.Yield();
            vehicle.Deform(new Vector3(-0.1387851f, -2.303327f, 0.1561583f), 50f, 100f);
            vehicle.Deform(new Vector3(-0.3996978f, -2.323202f, 0.4038336f), 50f, 100f);
            vehicle.Deform(new Vector3(-0.6120571f, -2.353938f, 0.5906799f), 50f, 100f);
            vehicle.Deform(new Vector3(-0.6288974f, -2.355043f, 0.6504698f), 50f, 100f);
            vehicle.Deform(new Vector3(0.8306209f, -0.5140075f, -0.02140887f), 50f, 100f);
            GameFiber.Yield();
            vehicle.Deform(new Vector3(0.8308675f, -0.3194958f, 0.1314263f), 50f, 100f);
            vehicle.Deform(new Vector3(-0.5114394f, -2.034291f, 1.136037f), 50f, 100f);
            vehicle.Deform(new Vector3(-0.5503472f, -1.994765f, 1.197578f), 50f, 100f);
            vehicle.Deform(new Vector3(-0.5496096f, -1.952912f, 1.222797f), 50f, 100f);
            vehicle.Deform(new Vector3(-0.5424793f, -1.92361f, 1.223951f), 50f, 100f);
            GameFiber.Yield();
            vehicle.Deform(new Vector3(-0.5356563f, -1.91957f, 1.224107f), 50f, 100f);
            vehicle.Deform(new Vector3(-0.5064003f, -1.891556f, 1.225208f), 50f, 100f);
            vehicle.Deform(new Vector3(-0.4403886f, -1.923975f, 1.223936f), 50f, 100f);
            vehicle.Deform(new Vector3(-0.3306132f, -1.991029f, 1.203477f), 50f, 100f);
            vehicle.Deform(new Vector3(-0.1367575f, -2.003275f, 1.140234f), 50f, 100f);
            GameFiber.Yield();
            vehicle.Deform(new Vector3(0.8310509f, -1.050368f, 0.6611637f), 50f, 100f);
            vehicle.Deform(new Vector3(0.8311118f, -1.065949f, 0.6641567f), 50f, 100f);
            vehicle.Deform(new Vector3(0.8311754f, -1.142461f, 0.6789015f), 50f, 100f);
            vehicle.Deform(new Vector3(0.8513352f, -1.217448f, 0.6767592f), 50f, 100f);
            vehicle.Deform(new Vector3(0.1674281f, -2.361258f, 0.7160065f), 50f, 100f);
            GameFiber.Yield();
            vehicle.Deform(new Vector3(0.1961935f, -2.349103f, 0.6615642f), 50f, 100f);
            vehicle.Deform(new Vector3(0.201108f, -2.345458f, 0.645283f), 50f, 100f);
            vehicle.Deform(new Vector3(0.1991418f, -2.337252f, 0.6084443f), 50f, 100f);
            vehicle.Deform(new Vector3(0.1846981f, -2.333486f, 0.5916543f), 50f, 100f);
            vehicle.Deform(new Vector3(0.1653983f, -2.333517f, 0.591709f), 50f, 100f);
            GameFiber.Yield();
            vehicle.Deform(new Vector3(0.1304869f, -2.331099f, 0.5806355f), 50f, 100f);
            vehicle.Deform(new Vector3(0.118933f, -2.324824f, 0.5525967f), 50f, 100f);
            vehicle.Deform(new Vector3(0.1028892f, -2.32234f, 0.5414394f), 50f, 100f);
            vehicle.Deform(new Vector3(0.09551498f, -2.317687f, 0.5206175f), 50f, 100f);
            vehicle.Deform(new Vector3(0.0868478f, -2.314352f, 0.505423f), 50f, 100f);
            GameFiber.Yield();
            vehicle.Deform(new Vector3(0.07793688f, -2.310951f, 0.4901747f), 50f, 100f);
            vehicle.Deform(new Vector3(0.05900618f, -2.311047f, 0.4906003f), 50f, 100f);
            vehicle.Deform(new Vector3(0.06035715f, -2.309729f, 0.4847719f), 50f, 100f);
            vehicle.Deform(new Vector3(0.05556415f, -2.308594f, 0.4868382f), 50f, 100f);
            vehicle.Deform(new Vector3(0.05556415f, -2.308594f, 0.4868382f), 50f, 100f);
            GameFiber.Yield();
            vehicle.Deform(new Vector3(0.04394671f, -2.307798f, 0.4832165f), 50f, 100f);
            vehicle.Deform(new Vector3(0.04074925f, -2.304858f, 0.4698107f), 50f, 100f);
            vehicle.Deform(new Vector3(0.03423458f, -2.298983f, 0.4427786f), 50f, 100f);
            vehicle.Deform(new Vector3(0.055994f, -2.283823f, 0.3721674f), 50f, 100f);
            vehicle.Deform(new Vector3(0.1049804f, -2.265763f, 0.2203315f), 50f, 100f);
            GameFiber.Yield();
            vehicle.Deform(new Vector3(0.1098974f, -2.292612f, 0.138626f), 50f, 100f);
            vehicle.Deform(new Vector3(0.09569795f, -2.303206f, 0.1031828f), 50f, 100f);
            vehicle.Deform(new Vector3(0.08150079f, -2.308563f, 0.08159151f), 50f, 100f);
            vehicle.Deform(new Vector3(0.07043803f, -2.306298f, 0.08193737f), 50f, 100f);
            vehicle.Deform(new Vector3(0.06890146f, -2.307463f, 0.07797761f), 50f, 100f);
            GameFiber.Yield();
            vehicle.Deform(new Vector3(0.07308098f, -2.303941f, 0.07355683f), 50f, 100f);
            vehicle.Deform(new Vector3(0.07308098f, -2.303941f, 0.07355683f), 50f, 100f);
            vehicle.Deform(new Vector3(0.05888136f, -2.304032f, 0.06601317f), 50f, 100f);
            vehicle.Deform(new Vector3(-0.07965675f, -2.312608f, 0.05609224f), 50f, 100f);
            vehicle.Deform(new Vector3(-0.2396466f, -2.341631f, 0.06738614f), 50f, 100f);
            GameFiber.Yield();
            vehicle.Deform(new Vector3(-0.2974242f, -2.35036f, 0.07583719f), 50f, 100f);
            vehicle.Deform(new Vector3(-0.3091002f, -2.354922f, 0.07057694f), 50f, 100f);
            vehicle.Deform(new Vector3(-0.3164162f, -2.356174f, 0.07125298f), 50f, 100f);
            vehicle.Deform(new Vector3(-0.3164162f, -2.356174f, 0.07125298f), 50f, 100f);
            vehicle.Deform(new Vector3(-0.3164162f, -2.356174f, 0.07125298f), 50f, 100f);
            GameFiber.Yield();
            vehicle.Deform(new Vector3(-0.3164162f, -2.356174f, 0.07125298f), 50f, 100f);
            vehicle.Deform(new Vector3(-0.3164162f, -2.356174f, 0.07125298f), 50f, 100f);
            vehicle.Deform(new Vector3(-0.3104536f, -2.350111f, 0.06335635f), 50f, 100f);
            vehicle.Deform(new Vector3(-0.6186939f, -2.37179f, 0.1740063f), 50f, 100f);
            vehicle.Deform(new Vector3(-0.6186939f, -2.37179f, 0.1740063f), 50f, 100f);
            GameFiber.Yield();
            vehicle.Deform(new Vector3(-0.6186939f, -2.37179f, 0.1740063f), 50f, 100f);
            vehicle.Deform(new Vector3(-0.6186939f, -2.37179f, 0.1740063f), 50f, 100f);
            vehicle.Deform(new Vector3(-0.6186939f, -2.37179f, 0.1740063f), 50f, 100f);
            vehicle.Deform(new Vector3(-0.6186939f, -2.37179f, 0.1740063f), 50f, 100f);
            vehicle.Deform(new Vector3(-0.6186939f, -2.37179f, 0.1740063f), 50f, 100f);
            GameFiber.Yield();
            vehicle.Deform(new Vector3(-0.6186939f, -2.37179f, 0.1740063f), 50f, 100f);
            vehicle.Deform(new Vector3(-0.6186939f, -2.37179f, 0.1740063f), 50f, 100f);
            vehicle.Deform(new Vector3(-0.6186939f, -2.37179f, 0.1740063f), 50f, 100f);
            vehicle.Deform(new Vector3(-0.6186939f, -2.37179f, 0.1740063f), 50f, 100f);
            vehicle.Deform(new Vector3(-0.6332624f, -2.375952f, 0.1726828f), 50f, 100f);
            GameFiber.Yield();
            vehicle.Deform(new Vector3(-0.6823096f, -2.389333f, 0.1693309f), 50f, 100f);
            vehicle.Deform(new Vector3(-0.7162996f, -2.394752f, 0.175165f), 50f, 100f);
            vehicle.Deform(new Vector3(-0.7162996f, -2.394752f, 0.175165f), 50f, 100f);
            vehicle.Deform(new Vector3(-0.7162996f, -2.394752f, 0.175165f), 50f, 100f);
            vehicle.Deform(new Vector3(-0.7162996f, -2.394752f, 0.175165f), 50f, 100f);
            GameFiber.Yield();
            vehicle.Deform(new Vector3(-0.7162996f, -2.394752f, 0.175165f), 50f, 100f);
            vehicle.Deform(new Vector3(-0.7162996f, -2.394752f, 0.175165f), 50f, 100f);
            vehicle.Deform(new Vector3(-0.2218222f, -2.324606f, 0.05061156f), 50f, 100f);
            vehicle.Deform(new Vector3(-0.1867279f, -2.316065f, 0.05421996f), 50f, 100f);
            vehicle.Deform(new Vector3(-0.07400177f, -2.296378f, 0.04941106f), 50f, 100f);
            GameFiber.Yield();
            vehicle.Deform(new Vector3(0.1049804f, -2.289763f, 0.08548115f), 50f, 100f);
            vehicle.Deform(new Vector3(0.2627574f, -2.306267f, 0.1297987f), 50f, 100f);
            vehicle.Deform(new Vector3(0.3756055f, -2.339123f, 0.1171506f), 50f, 100f);
            vehicle.Deform(new Vector3(0.4585818f, -2.363341f, 0.1078487f), 50f, 100f);
            vehicle.Deform(new Vector3(0.5120553f, -2.376048f, 0.1053553f), 50f, 100f);
            GameFiber.Yield();
            vehicle.Deform(new Vector3(0.5535421f, -2.380179f, 0.1175364f), 50f, 100f);
            vehicle.Deform(new Vector3(0.5985346f, -2.388107f, 0.1239534f), 50f, 100f);
            vehicle.Deform(new Vector3(0.5985346f, -2.388107f, 0.1239534f), 50f, 100f);
            vehicle.Deform(new Vector3(0.5985346f, -2.388107f, 0.1239534f), 50f, 100f);
            vehicle.Deform(new Vector3(0.5985346f, -2.388107f, 0.1239534f), 50f, 100f);
            GameFiber.Yield();
            vehicle.Deform(new Vector3(-0.2755421f, -2.313343f, 0.595105f), 50f, 100f);
            vehicle.Deform(new Vector3(-0.2813806f, -2.314291f, 0.5986146f), 50f, 100f);
            vehicle.Deform(new Vector3(-0.2813806f, -2.314291f, 0.5986146f), 50f, 100f);
            vehicle.Deform(new Vector3(-0.2813806f, -2.314291f, 0.5986146f), 50f, 100f);
            vehicle.Deform(new Vector3(-0.2813806f, -2.314291f, 0.5986146f), 50f, 100f);
            GameFiber.Yield();
            vehicle.Deform(new Vector3(-0.2813806f, -2.314291f, 0.5986146f), 50f, 100f);
            vehicle.Deform(new Vector3(-0.2813806f, -2.314291f, 0.5986146f), 50f, 100f);
            vehicle.Deform(new Vector3(-0.2813806f, -2.314291f, 0.5986146f), 50f, 100f);
            vehicle.Deform(new Vector3(-0.2813806f, -2.314291f, 0.5986146f), 50f, 100f);
            vehicle.Deform(new Vector3(-0.2813806f, -2.314291f, 0.5986146f), 50f, 100f);
            GameFiber.Yield();
            vehicle.Deform(new Vector3(-0.2813806f, -2.314291f, 0.5986146f), 50f, 100f);
            vehicle.Deform(new Vector3(0.4524969f, -2.305933f, 0.3087983f), 50f, 100f);
            vehicle.Deform(new Vector3(0.4374989f, -2.302623f, 0.3633786f), 50f, 100f);
            vehicle.Deform(new Vector3(0.3991461f, -2.29396f, 0.4384923f), 50f, 100f);
            vehicle.Deform(new Vector3(0.4024045f, -2.294786f, 0.5057266f), 50f, 100f);
            GameFiber.Yield();
            vehicle.Deform(new Vector3(0.4204121f, -2.310586f, 0.5596256f), 50f, 100f);
            vehicle.Deform(new Vector3(0.4359623f, -2.322066f, 0.5978205f), 50f, 100f);
            vehicle.Deform(new Vector3(0.445367f, -2.326324f, 0.6119156f), 50f, 100f);
            vehicle.Deform(new Vector3(0.447272f, -2.327703f, 0.6165683f), 50f, 100f);
            vehicle.Deform(new Vector3(0.4491774f, -2.329107f, 0.6212004f), 50f, 100f);
            GameFiber.Yield();
            vehicle.Deform(new Vector3(0.4491774f, -2.329107f, 0.6212004f), 50f, 100f);
            vehicle.Deform(new Vector3(0.4491774f, -2.329107f, 0.6212004f), 50f, 100f);
            vehicle.Deform(new Vector3(0.4323376f, -2.324793f, 0.6071072f), 50f, 100f);
            vehicle.Deform(new Vector3(0.3703806f, -2.30428f, 0.5393847f), 50f, 100f);
            vehicle.Deform(new Vector3(0.293428f, -2.27473f, 0.4416108f), 50f, 100f);
            GameFiber.Yield();
        }
        private static void DeformFront(Vehicle vehicleToDamage)
        {
            Vehicle vehicle = vehicleToDamage;
            vehicle.Repair();
            GameFiber.Yield();
            //vehicle.Deform(new Vector3(-0.04726308f, 2.160486f, 0.4337103f), 236f, 57f);
            //vehicle.Deform(new Vector3(-0.04726308f, 2.160486f, 0.4337103f), 236f, 57f);
            //vehicle.Deform(new Vector3(-0.04726308f, 2.160486f, 0.4337103f), 236f, 57f);
            //vehicle.Deform(new Vector3(-0.04726308f, 2.160486f, 0.4337103f), 236f, 57f);
            //vehicle.Deform(new Vector3(-0.04726308f, 2.160486f, 0.4337103f), 236f, 57f);
            //vehicle.Deform(new Vector3(-0.04726308f, 2.160486f, 0.4337103f), 236f, 57f);
            //GameFiber.Yield();
            //vehicle.Deform(new Vector3(-0.04726308f, 2.160486f, 0.4337103f), 236f, 57f);
            //vehicle.Deform(new Vector3(-0.04726308f, 2.160486f, 0.4337103f), 236f, 57f);
            //vehicle.Deform(new Vector3(-0.04726308f, 2.160486f, 0.4337103f), 236f, 57f);
            //vehicle.Deform(new Vector3(-0.04726308f, 2.160486f, 0.4337103f), 236f, 57f);
            //vehicle.Deform(new Vector3(-0.04726308f, 2.160486f, 0.4337103f), 236f, 57f);
            //GameFiber.Yield();
            //vehicle.Deform(new Vector3(-0.04726308f, 2.160486f, 0.4337103f), 236f, 57f);
            //vehicle.Deform(new Vector3(-0.04726308f, 2.160486f, 0.4337103f), 236f, 57f);
            //vehicle.Deform(new Vector3(0.1467158f, 1.992459f, 0.2372238f), 236f, 57f);
            //vehicle.Deform(new Vector3(0.1467158f, 1.992459f, 0.2372238f), 236f, 57f);
            //vehicle.Deform(new Vector3(0.1467158f, 1.992459f, 0.2372238f), 236f, 57f);
            //GameFiber.Yield();
            //vehicle.Deform(new Vector3(0.1467158f, 1.992459f, 0.2372238f), 236f, 57f);
            //vehicle.Deform(new Vector3(0.1467158f, 1.992459f, 0.2372238f), 236f, 57f);
            //vehicle.Deform(new Vector3(-0.5453222f, 2.030332f, 0.2721904f), 236f, 57f);
            //vehicle.Deform(new Vector3(-0.5453222f, 2.030332f, 0.2721904f), 236f, 57f);
            //vehicle.Deform(new Vector3(-0.5453222f, 2.030332f, 0.2721904f), 236f, 57f);
            //GameFiber.Yield();
            //vehicle.Deform(new Vector3(-0.5453222f, 2.030332f, 0.2721904f), 236f, 57f);
            //vehicle.Deform(new Vector3(-0.5453222f, 2.030332f, 0.2721904f), 236f, 57f);
            //vehicle.Deform(new Vector3(0.6407732f, 2.107275f, 0.2052808f), 236f, 57f);
            //vehicle.Deform(new Vector3(0.6407732f, 2.107275f, 0.2052808f), 236f, 57f);
            //vehicle.Deform(new Vector3(0.6407732f, 2.107275f, 0.2052808f), 236f, 57f);
            //GameFiber.Yield();            
            vehicle.Deform(new Vector3(-0.04726308f, 2.160486f, 0.4337103f), 236f, 57f);
            vehicle.Deform(new Vector3(-0.04726308f, 2.160486f, 0.4337103f), 236f, 57f);
            vehicle.Deform(new Vector3(-0.04726308f, 2.160486f, 0.4337103f), 236f, 57f);
            vehicle.Deform(new Vector3(-0.04726308f, 2.160486f, 0.4337103f), 236f, 57f);
            vehicle.Deform(new Vector3(-0.04726308f, 2.160486f, 0.4337103f), 236f, 57f);
            vehicle.Deform(new Vector3(-0.04726308f, 2.160486f, 0.4337103f), 236f, 57f);
            GameFiber.Yield();
            vehicle.Deform(new Vector3(-0.04726308f, 2.160486f, 0.4337103f), 236f, 57f);
            vehicle.Deform(new Vector3(-0.04726308f, 2.160486f, 0.4337103f), 236f, 57f);
            vehicle.Deform(new Vector3(-0.04726308f, 2.160486f, 0.4337103f), 236f, 57f);
            vehicle.Deform(new Vector3(-0.04726308f, 2.160486f, 0.4337103f), 236f, 57f);
            vehicle.Deform(new Vector3(-0.04726308f, 2.160486f, 0.4337103f), 236f, 57f);
            GameFiber.Yield();
            vehicle.Deform(new Vector3(-0.04726308f, 2.160486f, 0.4337103f), 236f, 57f);
            vehicle.Deform(new Vector3(-0.04726308f, 2.160486f, 0.4337103f), 236f, 57f);
            vehicle.Deform(new Vector3(0.1467158f, 1.992459f, 0.2372238f), 236f, 57f);
            vehicle.Deform(new Vector3(0.1467158f, 1.992459f, 0.2372238f), 236f, 57f);
            vehicle.Deform(new Vector3(0.1467158f, 1.992459f, 0.2372238f), 236f, 57f);
            GameFiber.Yield();
            vehicle.Deform(new Vector3(0.1467158f, 1.992459f, 0.2372238f), 236f, 57f);
            vehicle.Deform(new Vector3(0.1467158f, 1.992459f, 0.2372238f), 236f, 57f);
            vehicle.Deform(new Vector3(-0.5453222f, 2.030332f, 0.2721904f), 236f, 57f);
            vehicle.Deform(new Vector3(-0.5453222f, 2.030332f, 0.2721904f), 236f, 57f);
            vehicle.Deform(new Vector3(-0.5453222f, 2.030332f, 0.2721904f), 236f, 57f);
            GameFiber.Yield();
            vehicle.Deform(new Vector3(-0.5453222f, 2.030332f, 0.2721904f), 236f, 57f);
            vehicle.Deform(new Vector3(-0.5453222f, 2.030332f, 0.2721904f), 236f, 57f);
            vehicle.Deform(new Vector3(0.6407732f, 2.107275f, 0.2052808f), 236f, 57f);
            vehicle.Deform(new Vector3(0.6407732f, 2.107275f, 0.2052808f), 236f, 57f);
            vehicle.Deform(new Vector3(0.6407732f, 2.107275f, 0.2052808f), 236f, 57f);
            GameFiber.Yield();
        }

        private void TaskIdentifySecondPed()
        {
            LogTrivialDebug_withAiC("DEBUG: TaskIdentifySecondPed() entered");
            GameFiber.Sleep(500);                                                                                                           //still nessesarry? hier war einmal das ped 1 sich vom streit entfernt
            foreach (var officer in UnitOfficers) officer.Tasks.PlayAnimation(new AnimationDictionary("amb@code_human_wander_idles_cop@male@static"), "static", 1f, AnimationFlags.Loop);

            //When player is just right before arriving cops position
            GameFiber.SleepUntil(() => Game.LocalPlayer.Character.DistanceTo(UnitOfficers[0].Position) < 7f && Game.LocalPlayer.Character.IsOnFoot, 0);
            if (UnitOfficers.Count > 1) UnitOfficers[1].PlayAmbientSpeech(null, "SETTLE_DOWN", 0, SpeechModifier.Force);

            //When player arrives Cop turns to player
            GameFiber.SleepUntil(() => Game.LocalPlayer.Character.DistanceTo(UnitOfficers[0].Position) < 3f && Game.LocalPlayer.Character.IsOnFoot, 0);
            Rage.Native.NativeFunction.Natives.x5AD23D40115353AC(UnitOfficers[0], Game.LocalPlayer.Character, 0);      //TASK_TURN_PED_TO_FACE_ENTITY
            GameFiber.Sleep(1000);
            UnitOfficers[0].PlayAmbientSpeech(null, "GENERIC_HI", 0, SpeechModifier.Force);
            Game.DisplaySubtitle("~b~Officer~w~: Hey. Can you check the other one?", 6000);
            //Cop Faces his suspect
            GameFiber.Sleep(6000);
            Rage.Native.NativeFunction.Natives.x5AD23D40115353AC(UnitOfficers[0], Suspects[0], 0);      //TASK_TURN_PED_TO_FACE_ENTITY
            GameFiber.Sleep(600);
            UnitOfficers[0].Tasks.PlayAnimation(new AnimationDictionary("amb@code_human_wander_idles_cop@male@static"), "static", 1f, AnimationFlags.Loop);
            if (UnitOfficers.Count != 1) UnitOfficers[1].PlayAmbientSpeech(null, "SETTLE_DOWN", 0, SpeechModifier.Force);

            //When player reaches the suspect
            GameFiber.SleepUntil(() => Game.LocalPlayer.Character.DistanceTo(Suspects[1].Position) < 3f, 0);
            Debug.DrawArrow(Suspects[1].GetOffsetPositionUp(3f), Suspects[1].Position, new Rotator(0f, 0f, 0f), 1f, System.Drawing.Color.Yellow); // unfertig
            Rage.Native.NativeFunction.Natives.x5AD23D40115353AC(Suspects[1], Game.LocalPlayer.Character, 0);      //TASK_TURN_PED_TO_FACE_ENTITY
            GameFiber.Sleep(1000);
            Game.DisplaySubtitle("~o~Suspect~w~: Hey", 4000);
            Suspects[1].PlayAmbientSpeech(null, "GENERIC_HI", 0, SpeechModifier.Force);
            GameFiber.Sleep(5000);

            LogTrivialDebug_withAiC(" DEBUG: choose what should happen");
            switch (new Random().Next(0, 3))
            {
                case 0:
                    Suspect1Flees();
                    break;
                case 1:
                    Suspect2Flees();
                    break;
                case 2:
                    NothingHappens();
                    break;
            }
        }

        private void TaskJustGiveCover()
        {
            LogTrivialDebug_withAiC(" DEBUG: TaskJustGiveCover() entered");
            GameFiber.SleepUntil(() => Game.LocalPlayer.Character.DistanceTo(UnitOfficers[0].Position) < 10f && Game.LocalPlayer.Character.IsOnFoot, 0);
            Game.DisplaySubtitle("~b~Officer~w~: Hey. Just cover us. " + (UnitOfficers.Count == 1 ? "I" : "We") + " handle this!", 6000);

            GameFiber.Sleep(20000);
            NothingHappens();
        }
        private void Suspect1Flees()
        {
            GameFiber.Sleep(20000);
            LogTrivialDebug_withAiC(" DEBUG: Suspect1Flees() entered");
            LHandle pursuit = LSPDFR_Functions.CreatePursuit();
            LSPDFR_Functions.AddPedToPursuit(pursuit, Suspects[0]);
            GameFiber.Sleep(1500);
            foreach (var officer in UnitOfficers) { LSPDFR_Functions.AddCopToPursuit(pursuit, officer); };
            UnitOfficers[0].PlayAmbientSpeech(null, "FOOT_CHASE", 0, SpeechModifier.Force);
            GameFiber.Sleep(12000);
            LSPDFR_Functions.SetPursuitIsActiveForPlayer(pursuit, true);
            AiCandHA_AddHelicopterToPursuit(pursuit);
            GameFiber.SleepWhile(() => LSPDFR_Functions.IsPursuitStillRunning(pursuit), 0);
            //ISSUE: Officers & Peds get Dismissed before the Arrest is fullfilled.
        }

        private void Suspect2Flees()
        {
            LogTrivialDebug_withAiC(" DEBUG: Suspect2Flees() entered");
            LSPDFR_Functions.AddPedContraband(Suspects[1], LSPD_First_Response.Engine.Scripting.Entities.ContrabandType.Narcotics, "Heroin");

            int i = 0;
            while (!LSPDFR_Functions.IsPedBeingFrisked(Suspects[1]) && i <= 2 * 60/*sekunden*/   )
            {
                i++;
                GameFiber.Sleep(500);
            }

            if (LSPDFR_Functions.IsPedArrested(Suspects[1]))
            {
                Game.DisplaySubtitle("~b~Officer~w~: Great. " + (UnitOfficers.Count == 1 ? "I" : "We") + " finished here too. Thanks for the Backup", 6000);
            }
            else
            {
                if (i >= 2 * 60) { Game.DisplaySubtitle("~b~Officer~w~: Would you PLEASE frisk your suspect! We can't stay here all day.", 6000); }

                while (!LSPDFR_Functions.IsPedBeingFrisked(Suspects[1])) { GameFiber.Sleep(500); }
                LHandle pursuit = LSPDFR_Functions.CreatePursuit();
                LSPDFR_Functions.AddPedToPursuit(pursuit, Suspects[1]);
                GameFiber.Sleep(1800);
                LSPDFR_Functions.SetPursuitIsActiveForPlayer(pursuit, true);
                if (UnitOfficers.Count > 1) LSPDFR_Functions.AddCopToPursuit(pursuit, UnitOfficers[1]);
                AiCandHA_AddHelicopterToPursuit(pursuit);
                GameFiber.SleepWhile(() => LSPDFR_Functions.IsPursuitStillRunning(pursuit), 0);
            }
        }

        private void NothingHappens()
        {
            LogTrivialDebug_withAiC(" DEBUG: NothingHappens() entered");
            for (int i = 0; i < 46/*seconds*/; i++) if (!Game.IsPaused && !Rage.Native.NativeFunction.Natives.IS_PAUSE_MENU_ACTIVE<bool>()) { i++; GameFiber.Sleep(1000); }
            Rage.Native.NativeFunction.Natives.x5AD23D40115353AC(UnitOfficers[0], Game.LocalPlayer.Character, 0);      //TASK_TURN_PED_TO_FACE_ENTITY
            GameFiber.Sleep(1000);
            UnitOfficers[1].PlayAmbientSpeech(null, "GENERIC_THANKS", 0, SpeechModifier.Force); 
            Game.DisplaySubtitle("~b~Officer~w~: We are done. Thanks for your help.", 3000);
            GameFiber.Sleep(3000);


            //Peds Leave, Cops Aproach own vehicle
            Game.LogTrivialDebug($"[AmbientAICallouts] [AiCallout MVA] DEBUG: Scene cleared");
            AiCandHA_DismissHelicopter();
            foreach (var suspect in Suspects) if (suspect) { suspect.Tasks.Clear(); suspect.Dismiss(); GameFiber.Sleep(6000); }
            for (int i = 1; i < UnitOfficers.Count; i++) { UnitOfficers[i].Tasks.Clear(); }
            if (UnitOfficers.Count != 1)
            {
                var officerAtCopCar = UnitOfficers[0].Tasks.FollowNavigationMeshToPosition(Unit.GetOffsetPosition(new Vector3(2.7f, 0f, 0f)), heading + 0f, 0.75f);
                UnitOfficers[1].Tasks.FollowNavigationMeshToPosition(Unit.GetOffsetPosition(new Vector3(2.2f, 1.3f, 0f)), heading + 180f, 0.75f).WaitForCompletion();
                while (officerAtCopCar.IsActive) { GameFiber.Sleep(300); }
                Rage.Native.NativeFunction.Natives.x5AD23D40115353AC(UnitOfficers[0], UnitOfficers[1], 0);      //TASK_TURN_PED_TO_FACE_ENTITY
                Rage.Native.NativeFunction.Natives.x5AD23D40115353AC(UnitOfficers[1], UnitOfficers[0], 0);      //TASK_TURN_PED_TO_FACE_ENTITY
                GameFiber.Sleep(16000);
            }
            //if (cone0) cone0.Delete();
            //if (cone1) cone1.Delete();      //is in another line for isPlayerResonponding in addition
            //if (cone2) cone2.Delete();
            finished = true;
            EnterAndDismiss();
        }
        private static bool IsExternalPluginRunning(string plugin, Version minimumVersion)
        {
            foreach (Assembly assembly in LSPD_First_Response.Mod.API.Functions.GetAllUserPlugins())
            {
                AssemblyName name = assembly.GetName();
                if (name.Name.Equals(plugin, StringComparison.OrdinalIgnoreCase))
                {
                    return (name.Version.CompareTo(minimumVersion) >= 0);
                }
            }
            return false;
        }
    }
}