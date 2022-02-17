using System;
using System.Collections.Generic;
using System.Linq;
using Rage;
using System.Reflection;
using LSPD_First_Response.Mod.API;
using LSPDFR_Functions = LSPD_First_Response.Mod.API.Functions;
using NativeFunction = Rage.Native.NativeFunction;
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
        private Version stpVersion = new Version("4.9.4.7");
        private bool isSTPRunning = false;
        internal bool suspectFirskedOverSTP = false;
        internal bool suspectArrestedOverSTP = false;

        public override bool Setup()
        {
            try
            {
                SceneInfo = "Motor Vehicle Accident";
                CalloutDetailsString = "MOTOR_VEHICLE_ACCIDENT";
                if (IsExternalPluginRunning("StopThePed", stpVersion)) isSTPRunning = true;
                Vector3 roadside = new Vector3();
                bool posFound = false;
                int trys = 0;
                while (!posFound)
                {
                    roadside = World.GetNextPositionOnStreet(Game.LocalPlayer.Character.Position.Around(AmbientAICallouts.API.Functions.minimumAiCalloutDistance + 10f, AmbientAICallouts.API.Functions.maximumAiCalloutDistance - 10f));


                    //isSTPRunning = IsExternalPluginRunning("StopThePed", new Version("4.9.3.5"));
                    //Game.LogTrivial("[AmbientAICallouts] [initialization] INFO: Detection - StopThePed: " + isSTPRunning);

                    Vector3 irrelevant;
                    heading = 0f;       //vieleicht guckt der MVA dann in fahrtrichtung der unit

                    //Rage.Native.NativeFunction.Natives.x240A18690AE96513<bool>(roadside.X, roadside.Y, roadside.Z, out roadside, 0, 3.0f, 0f);//GET_CLOSEST_VEHICLE_NODE

                    Rage.Native.NativeFunction.Natives.xFF071FB798B803B0<bool>(roadside.X, roadside.Y, roadside.Z, out irrelevant, out heading, 0, 3.0f, 0f); //GET_CLOSEST_VEHICLE_NODE_WITH_HEADING //Find Side of the road.
                    if (Rage.Native.NativeFunction.Natives.xA0F8A7517A273C05<bool>(roadside.X, roadside.Y, roadside.Z, heading, out roadside)) Location = roadside; //_GET_ROAD_SIDE_POINT_WITH_HEADING


                    if (Functions.IsLocationAcceptedBySystem(Location))
                        posFound = true;
                    trys++;
                    if (trys >= 30) { LogTrivial_withAiC("ERROR: in AICallout object: At Setup(): unable to find safe coords for this event"); return false; }

                }

                //spawn 2 vehicles at the side of the road
                AmbientAICallouts.API.Helper.CleanArea(roadside, 25f);
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
                        List<string> idleAnims = new List<string>() { "idle_a", /*"idle_b",*/ "idle_c" };
                        while (!finished)
                        {                                                                                   //sollange call läuft //Workaround
                            while (!senarioTaskAsigned)
                            {
                                //try {
                                for (int i = 0; i < Suspects.Count; i++)
                                    if (Suspects[i] 
                                    && !senarioTaskAsigned 
                                    && !finished
                                    && !NativeFunction.Natives.GET_IS_TASK_ACTIVE<bool>(Suspects[i], 35)
                                    && !IsPedOccupiedbyLSPDFRInteraction(Suspects[i])
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
                if (!IsUnitInTime(Units[0], 100f, 130))  //if vehicle is never reaching its location
                {
                    Disregard();
                }
                else  //if vehicle is reaching its location
                {
                    //Waiting until the unit arrives
                    GameFiber.WaitWhile(() => Units[0].PoliceVehicle.Position.DistanceTo(Location) >= 65f, 25000);
                    Units[0].PoliceVehicle.IsSirenSilent = true;
                    Units[0].PoliceVehicle.TopSpeed = 16f;
                    OfficerReportOnScene(Units[0]);

                    GameFiber.WaitWhile(() => Units[0].PoliceVehicle.Position.DistanceTo(Location) >= 45f, 20000);
                    var unitTask = Units[0].UnitOfficers[0].Tasks.ParkVehicle(SuspectsVehicles[1].GetOffsetPositionFront(-9f), heading);

                    GameFiber.WaitWhile(() => unitTask.IsActive, 10000);
                    if (Units[0].PoliceVehicle.Position.DistanceTo(SuspectsVehicles[1].GetOffsetPositionFront(-9f)) >= 4f) Units[0].PoliceVehicle.Position = SuspectsVehicles[1].GetOffsetPositionFront(-9f); Units[0].PoliceVehicle.Heading = heading;
                    OfficersLeaveVehicle(Units[0], false);
                    var cone0 = new Rage.Object(new Model("prop_mp_cone_02"), Units[0].PoliceVehicle.GetOffsetPosition(new Vector3(-0.7f, -4f, 0f)), Units[0].PoliceVehicle.Heading); cone0.Position = cone0.GetOffsetPositionUp(-cone0.HeightAboveGround); cone0.IsPersistent = false;
                    var cone1 = new Rage.Object(new Model("prop_mp_cone_02"), Units[0].PoliceVehicle.GetOffsetPosition(new Vector3(0.0f, -5f, 0f)), Units[0].PoliceVehicle.Heading); cone1.Position = cone1.GetOffsetPositionUp(-cone1.HeightAboveGround); cone1.IsPersistent = false;
                    var cone2 = new Rage.Object(new Model("prop_mp_cone_02"), Units[0].PoliceVehicle.GetOffsetPosition(new Vector3(+0.7f, -6f, 0f)), Units[0].PoliceVehicle.Heading); cone2.Position = cone2.GetOffsetPositionUp(-cone2.HeightAboveGround); cone2.IsPersistent = false;

                    //Aproach the Suspects
                    LogTrivialDebug_withAiC($"DEBUG: Aproach");
                    for (int i = 0; i < Units[0].UnitOfficers.Count; i++) { Units[0].UnitOfficers[i].Tasks.FollowNavigationMeshToPosition(SuspectsVehicles[1].GetOffsetPosition(new Vector3(2.5f + i, -2f, 0f)), heading + 0f, 1f, 1f, 20000); }     //ich versuche hier so ein wenig abstand zwichen allen peds zu erstellen
                    GameFiber.WaitWhile(() => Units[0].UnitOfficers[0].Position.DistanceTo(SuspectsVehicles[1].RightPosition) > 8f, 25000);
                    //Suspects are getting in position
                    for (int i = 0; i < Suspects.Count; i++) { Suspects[i].Tasks.FollowNavigationMeshToPosition(SuspectsVehicles[1].GetOffsetPosition(new Vector3(2.5f + i, 0f, 0f)), heading + 180f, 1f, 1f, 20000); }    //ich versuche hier so ein wenig abstand zwichen allen peds zu erstellen
                    GameFiber.WaitUntil(() => Units[0].UnitOfficers[0].Tasks.CurrentTaskStatus == Rage.TaskStatus.NoTask, 15000);

                    // Wenn Cops nicht zeitig neber die autos kommen
                    for (int i = 0; i < Units[0].UnitOfficers.Count; i++)
                        if (Units[0].UnitOfficers[i].Position.DistanceTo(SuspectsVehicles[1].GetOffsetPosition(new Vector3(2f + i, 0f, 0f))) >= 3f)
                        {
                            Units[0].UnitOfficers[i].Position = SuspectsVehicles[1].GetOffsetPosition(new Vector3(2f + i, 0f, 0f));
                            Rage.Native.NativeFunction.Natives.x5AD23D40115353AC(Units[0].UnitOfficers[i], Suspects[i], 0);      //TASK_TURN_PED_TO_FACE_ENTITY
                        }

                    //Talk To The Suspects
                    LogTrivialDebug_withAiC($"DEBUG: Peds are Talking");
                    for (int i = 1; i < Units[0].UnitOfficers.Count; i++) { Units[0].UnitOfficers[i].Tasks.PlayAnimation(new AnimationDictionary("amb@code_human_wander_idles_cop@male@static"), "static", 1f, AnimationFlags.Loop); }

                    if (playerRespondingInAdditon)
                    {
                        Rage.Native.NativeFunction.Natives.x5AD23D40115353AC(Units[0].UnitOfficers[0], Suspects[0], 0);      //TASK_TURN_PED_TO_FACE_ENTITY
                        if (Units[0].UnitOfficers.Count > 1)
                        {
                            Suspects[1].Tasks.FollowNavigationMeshToPosition(SuspectsVehicles[0].RightPosition, SuspectsVehicles[0].Heading - 90f, 1f, 2f, 9000);
                            Rage.Native.NativeFunction.Natives.x5AD23D40115353AC(Units[0].UnitOfficers[1], Suspects[0], 0);      //TASK_TURN_PED_TO_FACE_ENTITY
                        }
                        for (int i = 1; i < Units[0].UnitOfficers.Count; i++) { Units[0].UnitOfficers[i].Tasks.PlayAnimation(new AnimationDictionary("amb@code_human_wander_idles_cop@male@static"), "static", 1f, AnimationFlags.Loop); }
                        
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
                            bool seperateThem = true;//randomizer.Next(0, 2) == 0;
                            var notebookAnimationFinished = false;
                            try
                            {
                                notepad = new Rage.Object("prop_notepad_02", Units[0].UnitOfficers[0].Position, 0f);
                                notepad.AttachTo(Units[0].UnitOfficers[0], Rage.Native.NativeFunction.Natives.GET_PED_BONE_INDEX<int>(Units[0].UnitOfficers[0], 18905), new Vector3(0.16f, 0.05f, -0.01f), new Rotator(-37f, -19f, .32f));

                                var taskPullsOutNotebook = Units[0].UnitOfficers[0].Tasks.PlayAnimation(new AnimationDictionary("amb@medic@standing@timeofdeath@enter"), "enter", 2f, AnimationFlags.None);
                                GameFiber.SleepUntil(() => taskPullsOutNotebook.CurrentTimeRatio > 0.92f, 10000);
                                Units[0].UnitOfficers[0].Tasks.PlayAnimation(new AnimationDictionary("amb@medic@standing@timeofdeath@base"), "base", 2f, AnimationFlags.Loop);
                                GameFiber.Sleep(4000);

                                var watchClock = Units[0].UnitOfficers[0].Tasks.PlayAnimation(new AnimationDictionary("amb@medic@standing@timeofdeath@idle_a"), "idle_b", 2f, AnimationFlags.None);
                                GameFiber.SleepUntil(() => watchClock.CurrentTimeRatio > 0.92f, 10000);

                                Units[0].UnitOfficers[0].Tasks.PlayAnimation(new AnimationDictionary("amb@medic@standing@timeofdeath@base"), "base", 2f, AnimationFlags.Loop);
                                GameFiber.Sleep(2500);
                                if (Units[0].UnitOfficers.Count != 1) Units[0].UnitOfficers[1].PlayAmbientSpeech(null, "SETTLE_DOWN", 0, SpeechModifier.Force);
                                GameFiber.Sleep(200);

                                //task ped to go to his car
                                if (seperateThem) {
                                    GameFiber.Sleep(400);
                                    Suspects[1].Tasks.FollowNavigationMeshToPosition(SuspectsVehicles[0].RightPosition, SuspectsVehicles[0].Heading - 90f, 1f, 2f, 9000);
                                }

                                var looksAround = Units[0].UnitOfficers[0].Tasks.PlayAnimation(new AnimationDictionary("amb@medic@standing@timeofdeath@idle_a"), "idle_c", 2f, AnimationFlags.None);
                                GameFiber.SleepUntil(() => looksAround.CurrentTimeRatio > 0.92f, 10000);

                                Units[0].UnitOfficers[0].Tasks.PlayAnimation(new AnimationDictionary("amb@medic@standing@timeofdeath@base"), "base", 2f, AnimationFlags.Loop);
                                GameFiber.Sleep(4000);

                                var putNotebookBack = Units[0].UnitOfficers[0].Tasks.PlayAnimation(new AnimationDictionary("amb@medic@standing@timeofdeath@exit"), "exit", 2f, AnimationFlags.None);
                                GameFiber.SleepUntil(() => !putNotebookBack.IsActive, 10000);
                                if (notepad) notepad.Delete();
                                Units[0].UnitOfficers[0].Tasks.Clear();
                                notebookAnimationFinished = true;
                            }
                            catch (Exception e) { LogTrivialDebug_withAiC($"ERROR: in while tasking Animation: {e}"); }



                            if (seperateThem)
                            {
                                try
                                {
                                    int count = 0;

                                    if (Suspects[0]) Suspects[0].Tasks.FollowNavigationMeshToPosition(SuspectsVehicles[1].RightPosition, SuspectsVehicles[1].Heading - 90f, 1f, 2f, 9000);
                                    GameFiber.Sleep(600);

                                    while (
                                        Units[0].UnitOfficers[0].Position.DistanceTo(SuspectsVehicles[0].GetOffsetPosition(new Vector3(2.5f + 1.8f, -1f, 0f))) > 2f 
                                     || Units[0].UnitOfficers[1].Position.DistanceTo(SuspectsVehicles[0].GetOffsetPosition(new Vector3(2.5f + 1.8f, +2f, 0f))) > 2f
                                     || count > 30
                                    )
                                    {

                                        if (!NativeFunction.Natives.GET_IS_TASK_ACTIVE<bool>(Units[0].UnitOfficers[0], 35))
                                            Units[0].UnitOfficers[0].Tasks.FollowNavigationMeshToPosition(SuspectsVehicles[0].GetOffsetPosition(new Vector3(2.5f + 1.8f, -1f, 0f)), heading + 69f, 1f, 1f, 20000);

                                        if (!NativeFunction.Natives.GET_IS_TASK_ACTIVE<bool>(Units[0].UnitOfficers[1], 35))
                                            Units[0].UnitOfficers[1].Tasks.FollowNavigationMeshToPosition(SuspectsVehicles[0].GetOffsetPosition(new Vector3(2.5f + 1.8f, +2f, 0f)), heading + 110f, 1f, 1f, 20000);  //Note: steht am weitesten vorne von allen personen am unfall
                                    
                                        count++;
                                        GameFiber.Sleep(1000);
                                    }

                                    Rage.Native.NativeFunction.Natives.x5AD23D40115353AC(Units[0].UnitOfficers[0], Suspects[1], 0);      //TASK_TURN_PED_TO_FACE_ENTITY
                                    Rage.Native.NativeFunction.Natives.x5AD23D40115353AC(Units[0].UnitOfficers[1], Suspects[1], 0);      //TASK_TURN_PED_TO_FACE_ENTITY

                                } catch (Exception e) { LogTrivialDebug_withAiC($"ERROR: in while tasking Animation: {e}"); }

                                notebookAnimationFinished = false;
                                try
                                {
                                    notepad = new Rage.Object("prop_notepad_02", Units[0].UnitOfficers[0].Position, 0f);
                                    notepad.AttachTo(Units[0].UnitOfficers[0], Rage.Native.NativeFunction.Natives.GET_PED_BONE_INDEX<int>(Units[0].UnitOfficers[0], 18905), new Vector3(0.16f, 0.05f, -0.01f), new Rotator(-37f, -19f, .32f));

                                    var taskPullsOutNotebook = Units[0].UnitOfficers[0].Tasks.PlayAnimation(new AnimationDictionary("amb@medic@standing@timeofdeath@enter"), "enter", 2f, AnimationFlags.None);
                                    GameFiber.SleepUntil(() => taskPullsOutNotebook.CurrentTimeRatio > 0.92f, 10000);
                                    Units[0].UnitOfficers[0].Tasks.PlayAnimation(new AnimationDictionary("amb@medic@standing@timeofdeath@base"), "base", 2f, AnimationFlags.Loop);
                                    GameFiber.Sleep(4000);

                                    var watchClock = Units[0].UnitOfficers[0].Tasks.PlayAnimation(new AnimationDictionary("amb@medic@standing@timeofdeath@idle_a"), "idle_b", 2f, AnimationFlags.None);
                                    GameFiber.SleepUntil(() => watchClock.CurrentTimeRatio > 0.92f, 10000);

                                    Units[0].UnitOfficers[0].Tasks.PlayAnimation(new AnimationDictionary("amb@medic@standing@timeofdeath@base"), "base", 2f, AnimationFlags.Loop);
                                    GameFiber.Sleep(2500);
                                    if (Units[0].UnitOfficers.Count != 1) Units[0].UnitOfficers[1].PlayAmbientSpeech(null, "SETTLE_DOWN", 0, SpeechModifier.Force);
                                    GameFiber.Sleep(200);

                                    var looksAround = Units[0].UnitOfficers[0].Tasks.PlayAnimation(new AnimationDictionary("amb@medic@standing@timeofdeath@idle_a"), "idle_c", 2f, AnimationFlags.None);
                                    GameFiber.SleepUntil(() => looksAround.CurrentTimeRatio > 0.92f, 10000);

                                    Units[0].UnitOfficers[0].Tasks.PlayAnimation(new AnimationDictionary("amb@medic@standing@timeofdeath@base"), "base", 2f, AnimationFlags.Loop);
                                    GameFiber.Sleep(4000);

                                    var putNotebookBack = Units[0].UnitOfficers[0].Tasks.PlayAnimation(new AnimationDictionary("amb@medic@standing@timeofdeath@exit"), "exit", 2f, AnimationFlags.None);
                                    GameFiber.SleepUntil(() => !putNotebookBack.IsActive, 10000);
                                    if (notepad) notepad.Delete();
                                    Units[0].UnitOfficers[0].Tasks.Clear();
                                    notebookAnimationFinished = true;
                                }
                                catch (Exception e) { LogTrivialDebug_withAiC($"ERROR: in while tasking Animation: {e}"); }
                            }

                            senarioTaskAsigned = true;

                            var ped0Status = LSPD_First_Response.Mod.API.Functions.GetPersonaForPed(Suspects[0]);
                            var ped1Status = LSPD_First_Response.Mod.API.Functions.GetPersonaForPed(Suspects[1]);

                            if (ped0Status.Wanted == true || ped0Status.ELicenseState != LSPD_First_Response.Engine.Scripting.Entities.ELicenseState.Valid
                             || ped1Status.Wanted == true || ped1Status.ELicenseState != LSPD_First_Response.Engine.Scripting.Entities.ELicenseState.Valid
                            )
                            {
                                var Arrest = LSPDFR_Functions.CreatePursuit();
                                LSPDFR_Functions.SetPursuitInvestigativeMode(Arrest, true);
                                foreach (var ofc in Units[0].UnitOfficers) LSPDFR_Functions.AddCopToPursuit(Arrest, ofc);
                                if (ped0Status.Wanted == true || ped0Status.ELicenseState != LSPD_First_Response.Engine.Scripting.Entities.ELicenseState.Valid) LSPDFR_Functions.AddPedToPursuit(Arrest, Suspects[0]);
                                if (ped1Status.Wanted == true || ped1Status.ELicenseState != LSPD_First_Response.Engine.Scripting.Entities.ELicenseState.Valid) LSPDFR_Functions.AddPedToPursuit(Arrest, Suspects[1]);
                                while(LSPDFR_Functions.IsPursuitStillRunning(Arrest)) { GameFiber.Sleep(500); }
                                GameFiber.Sleep(10000); //wait until ofc probably seated suspect
                            }




                            //Peds Leave, Cops Aproach own vehicle
                            Game.LogTrivialDebug($"[AmbientAICallouts] [AiCallout MVA] DEBUG: Scene cleared");
                            Functions.AiCandHA_DismissHelicopter(MO);
                            foreach (var suspect in Suspects) if (suspect) { if (!IsPedOccupiedbyLSPDFRInteraction(suspect)) { suspect.Tasks.Clear(); suspect.Dismiss(); GameFiber.Sleep(6000); } else { suspect.IsPersistent = false; } }
                            for (int i = 1; i < Units[0].UnitOfficers.Count; i++) { Units[0].UnitOfficers[i].Tasks.Clear(); }
                            if (Units[0].UnitOfficers.Count != 1)
                            {
                                var officerAtCopCar = Units[0].UnitOfficers[0].Tasks.FollowNavigationMeshToPosition(Units[0].PoliceVehicle.GetOffsetPosition(new Vector3(2.7f, 0f, 0f)), heading + 0f, 0.75f);
                                Units[0].UnitOfficers[1].Tasks.FollowNavigationMeshToPosition(Units[0].PoliceVehicle.GetOffsetPosition(new Vector3(2.2f, 1.3f, 0f)), heading + 180f, 0.75f).WaitForCompletion();
                                while (officerAtCopCar.IsActive) { GameFiber.Sleep(300); }
                                Rage.Native.NativeFunction.Natives.x5AD23D40115353AC(Units[0].UnitOfficers[0], Units[0].UnitOfficers[1], 0);      //TASK_TURN_PED_TO_FACE_ENTITY
                                Rage.Native.NativeFunction.Natives.x5AD23D40115353AC(Units[0].UnitOfficers[1], Units[0].UnitOfficers[0], 0);      //TASK_TURN_PED_TO_FACE_ENTITY
                                GameFiber.Sleep(16000);
                            }
                            if (cone0) cone0.Delete();
                            if (cone1) cone1.Delete();
                            if (cone2) cone2.Delete();
                            finished = true;
                            EnterAndDismiss(Units[0]);

                        }
                        else //Callout suspects are getting agressive 
                        {
                            LogTrivial_withAiC($"INFO: choosed callout path");
                            bool OffiverRequiringAssistanceUSED = false;

                            var taskPullsOutNotebook = Units[0].UnitOfficers[0].Tasks.PlayAnimation(new AnimationDictionary("amb@medic@standing@timeofdeath@enter"), "enter", 2f, AnimationFlags.None);
                            GameFiber.SleepUntil(() => taskPullsOutNotebook.CurrentTimeRatio > 0.92f, 10000);
                            Units[0].UnitOfficers[0].Tasks.PlayAnimation(new AnimationDictionary("amb@medic@standing@timeofdeath@base"), "base", 2f, AnimationFlags.Loop);

                            GameFiber.Sleep(15000);                     //Because of this delay here
                            Units[0].UnitOfficers[0].Tasks.Clear();
                            senarioTaskAsigned = false;
                            finished = true;
                            if (!LSPD_First_Response.Mod.API.Functions.IsCalloutRunning()) //we need to check again if the player is not in a call already so we wont take his ongoing call
                            {

                                switch (randomizer.Next(0, 6))
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
                                        Units[0].UnitOfficers[0].PlayAmbientSpeech(null, "SETTLE_DOWN", 0, SpeechModifier.Force);
                                        GameFiber.Sleep(2000);
                                        Suspects[1].PlayAmbientSpeech(null, "GENERIC_WHATEVER", 0, SpeechModifier.Force);
                                        GameFiber.Sleep(1000);
                                        Suspects[1].Tasks.FollowNavigationMeshToPosition(SuspectsVehicles[0].RightPosition, SuspectsVehicles[0].Heading - 90f, 1f, 2f, 9000);
                                        Rage.Native.NativeFunction.Natives.x5AD23D40115353AC(Units[0].UnitOfficers[0], Suspects[0], 0);      //TASK_TURN_PED_TO_FACE_ENTITY
                                        Rage.Native.NativeFunction.Natives.x5AD23D40115353AC(Units[0].UnitOfficers[1], Suspects[0], 0);      //TASK_TURN_PED_TO_FACE_ENTITY
                                        for (int i = 1; i < Units[0].UnitOfficers.Count; i++) { Units[0].UnitOfficers[i].Tasks.PlayAnimation(new AnimationDictionary("amb@code_human_wander_idles_cop@male@static"), "static", 1f, AnimationFlags.Loop); }

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
                                            notepad = new Rage.Object("prop_notepad_02", Units[0].UnitOfficers[0].Position, 0f);
                                            notepad.AttachTo(Units[0].UnitOfficers[0], Rage.Native.NativeFunction.Natives.GET_PED_BONE_INDEX<int>(Units[0].UnitOfficers[0], 18905), new Vector3(0.16f, 0.05f, -0.01f), new Rotator(-37f, -19f, .32f));

                                            if (Units[0].UnitOfficers[0])
                                                if (LSPDFR_Functions.IsCopBusy(Units[0].UnitOfficers[0], false))
                                                {
                                                    GameFiber.Sleep(4000);
                                                }

                                            if (Units[0].UnitOfficers[0])
                                                if (LSPDFR_Functions.IsCopBusy(Units[0].UnitOfficers[0], false))
                                                {
                                                    var watchClock = Units[0].UnitOfficers[0].Tasks.PlayAnimation(new AnimationDictionary("amb@medic@standing@timeofdeath@idle_a"), "idle_b", 2f, AnimationFlags.None);
                                                    GameFiber.SleepUntil(() => watchClock.CurrentTimeRatio > 0.92f, 10000);
                                                }

                                            if (Units[0].UnitOfficers[0])
                                                if (LSPDFR_Functions.IsCopBusy(Units[0].UnitOfficers[0], false))
                                                {
                                                    Units[0].UnitOfficers[0].Tasks.PlayAnimation(new AnimationDictionary("amb@medic@standing@timeofdeath@base"), "base", 2f, AnimationFlags.Loop);
                                                    GameFiber.Sleep(2500);
                                                    if (Units[0].UnitOfficers.Count != 1) Units[0].UnitOfficers[1].PlayAmbientSpeech(null, "SETTLE_DOWN", 0, SpeechModifier.Force);
                                                    GameFiber.Sleep(200);
                                                }

                                            if (Units[0].UnitOfficers[0])
                                                if (LSPDFR_Functions.IsCopBusy(Units[0].UnitOfficers[0], false))
                                                {
                                                    var looksAround = Units[0].UnitOfficers[0].Tasks.PlayAnimation(new AnimationDictionary("amb@medic@standing@timeofdeath@idle_a"), "idle_c", 2f, AnimationFlags.None);
                                                    GameFiber.SleepUntil(() => looksAround.CurrentTimeRatio > 0.92f, 10000);
                                                }

                                            if (Units[0].UnitOfficers[0])
                                                if (LSPDFR_Functions.IsCopBusy(Units[0].UnitOfficers[0], false))
                                                {
                                                    Units[0].UnitOfficers[0].Tasks.PlayAnimation(new AnimationDictionary("amb@medic@standing@timeofdeath@base"), "base", 2f, AnimationFlags.Loop);
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
                                    try { suspect.IsPersistent = false; 
                                    } catch { } 
                        } 
                    }
                    if (suspectsVehicleRecoveryVar.Any()) { 
                        foreach (var vehicle in suspectsVehicleRecoveryVar) { 
                            if (vehicle) 
                                if (vehicle.Position.DistanceTo(locationRecoveryVar) < 9f || vehicle.IsEmpty) 
                                    try { vehicle.IsPersistent = false; ; 
                                    } catch { } 
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
            foreach (var officer in Units[0].UnitOfficers) officer.Tasks.PlayAnimation(new AnimationDictionary("amb@code_human_wander_idles_cop@male@static"), "static", 1f, AnimationFlags.Loop);

            //When player is just right before arriving cops position
            GameFiber.SleepUntil(() => Game.LocalPlayer.Character.DistanceTo(Units[0].UnitOfficers[0].Position) < 7f && Game.LocalPlayer.Character.IsOnFoot, 0);
            if (Units[0].UnitOfficers.Count > 1) Units[0].UnitOfficers[1].PlayAmbientSpeech(null, "SETTLE_DOWN", 0, SpeechModifier.Force);

            //When player arrives Cop turns to player
            GameFiber.SleepUntil(() => Game.LocalPlayer.Character.DistanceTo(Units[0].UnitOfficers[0].Position) < 3f && Game.LocalPlayer.Character.IsOnFoot, 0);
            Rage.Native.NativeFunction.Natives.x5AD23D40115353AC(Units[0].UnitOfficers[0], Game.LocalPlayer.Character, 0);      //TASK_TURN_PED_TO_FACE_ENTITY
            GameFiber.Sleep(1000);
            Units[0].UnitOfficers[0].PlayAmbientSpeech(null, "GENERIC_HI", 0, SpeechModifier.Force);
            Game.DisplaySubtitle("~b~Officer~w~: Hey. Can you check the other one?", 5000);
            GameFiber.Sleep(3000);
            //Cop Faces his suspect
            Rage.Native.NativeFunction.Natives.x5AD23D40115353AC(Units[0].UnitOfficers[0], Suspects[0], 0);      //TASK_TURN_PED_TO_FACE_ENTITY
            GameFiber.Sleep(600);
            Units[0].UnitOfficers[0].Tasks.PlayAnimation(new AnimationDictionary("amb@code_human_wander_idles_cop@male@static"), "static", 1f, AnimationFlags.Loop);
            if (Units[0].UnitOfficers.Count != 1) Units[0].UnitOfficers[1].PlayAmbientSpeech(null, "SETTLE_DOWN", 0, SpeechModifier.Force);

            //When player reaches the suspect
            GameFiber.SleepUntil(() => Game.LocalPlayer.Character.DistanceTo(Suspects[1].Position) < 3f, 0);
            Debug.DrawArrow(Suspects[1].GetOffsetPositionUp(3f), Suspects[1].Position, new Rotator(0f, 0f, 0f), 1f, System.Drawing.Color.Yellow); // unfertig
            Rage.Native.NativeFunction.Natives.x5AD23D40115353AC(Suspects[1], Game.LocalPlayer.Character, 0);      //TASK_TURN_PED_TO_FACE_ENTITY
            GameFiber.Sleep(1000);
            Game.DisplaySubtitle("~o~Suspect~w~: Hey", 4000);
            Suspects[1].PlayAmbientSpeech(null, "GENERIC_HI", 0, SpeechModifier.Force);
            GameFiber.Sleep(3000);

            LogTrivialDebug_withAiC(" DEBUG: choose what should happen");
            switch (new Random().Next(0, 3))
            {
                case 0:
                    Suspect1Flees();
                    //EndOfPursuit();
                    break;
                case 1:
                    Suspect2Flees();
                    //EndOfPursuit();
                    break;
                case 2:
                    NothingHappens();
                    break;
            }


        }

        private void TaskJustGiveCover()
        {
            LogTrivialDebug_withAiC(" DEBUG: TaskJustGiveCover() entered");
            GameFiber.SleepUntil(() => Game.LocalPlayer.Character.DistanceTo(Units[0].UnitOfficers[0].Position) < 10f && Game.LocalPlayer.Character.IsOnFoot, 0);
            Game.DisplaySubtitle("~b~Officer~w~: Hey. Just cover us. " + (Units[0].UnitOfficers.Count == 1 ? "I" : "We") + " handle this!", 6000);

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
            foreach (var officer in Units[0].UnitOfficers) { LSPDFR_Functions.AddCopToPursuit(pursuit, officer); };
            Units[0].UnitOfficers[0].PlayAmbientSpeech(null, "FOOT_CHASE", 0, SpeechModifier.Force);
            GameFiber.Sleep(12000);
            LSPDFR_Functions.SetPursuitIsActiveForPlayer(pursuit, true);
            Functions.AiCandHA_AddHelicopterToPursuit(MO, pursuit);
            GameFiber.SleepWhile(() => LSPDFR_Functions.IsPursuitStillRunning(pursuit), 0);
            //ISSUE: Officers & Peds get Dismissed before the Arrest is fullfilled.
        }

        private void Suspect2Flees()
        {
            LogTrivialDebug_withAiC(" DEBUG: Suspect2Flees() entered");
            if (isSTPRunning) ExternalPluginSupport.logInEvents(this);          //Stp Support
            LSPDFR_Functions.AddPedContraband(Suspects[1], LSPD_First_Response.Engine.Scripting.Entities.ContrabandType.Narcotics, "Heroin");

            int i = 0;
            while (!LSPDFR_Functions.IsPedBeingFrisked(Suspects[1]) && !suspectFirskedOverSTP && i < 2 * 60/*sekunden*/   )
            {
                i++;
                GameFiber.Sleep(500);
            }

            if (LSPDFR_Functions.IsPedArrested(Suspects[1]) || suspectArrestedOverSTP)
            {
                Game.DisplaySubtitle("~b~Officer~w~: Great. " + (Units[0].UnitOfficers.Count == 1 ? "I" : "We") + " finished here too. Thanks for the Backup", 6000);
            }
            else
            {
                if (i >= 2 * 60) { Game.DisplaySubtitle("~b~Officer~w~: Would you PLEASE frisk your suspect! We can't stay here all day.", 6000); }
                while (!LSPDFR_Functions.IsPedBeingFrisked(Suspects[1]) && !suspectFirskedOverSTP)  { GameFiber.Sleep(500); }
                LHandle pursuit = LSPDFR_Functions.CreatePursuit();
                LSPDFR_Functions.AddPedToPursuit(pursuit, Suspects[1]);
                GameFiber.Sleep(1800);
                LSPDFR_Functions.SetPursuitIsActiveForPlayer(pursuit, true);
                if (Units[0].UnitOfficers.Count > 1) LSPDFR_Functions.AddCopToPursuit(pursuit, Units[0].UnitOfficers[1]);
                Functions.AiCandHA_AddHelicopterToPursuit(MO, pursuit);
                GameFiber.SleepWhile(() => LSPDFR_Functions.IsPursuitStillRunning(pursuit), 0);
            }
            if (isSTPRunning) try { ExternalPluginSupport.logOutEvents(this); } catch { }          //Stp Support
        }

        private void EndOfPursuit()
        {
            LogTrivialDebug_withAiC(" DEBUG: EndOfPursuit() entered");
            GameFiber.Sleep(3000);
            List<bool> anySuspectGrabbed = new List<bool> { false, false };
            if (ExternalPluginSupport.isPedBeeingGrabbedBySTP(Suspects[0]) || LSPDFR_Functions.IsPedBeingGrabbed(Suspects[0]))
                anySuspectGrabbed[0] = true;
            if (ExternalPluginSupport.isPedBeeingGrabbedBySTP(Suspects[1]) || LSPDFR_Functions.IsPedBeingGrabbed(Suspects[1]))
                anySuspectGrabbed[1] = true;

            //----------------------------------------------------needs more code to detect which suspect is free for entering its own vehicle

            //Peds Leave, Cops Aproach own vehicle
            Game.LogTrivialDebug($"[AmbientAICallouts] [AiCallout MVA] DEBUG: Scene cleared");
            Functions.AiCandHA_DismissHelicopter(MO);
            foreach (var suspect in Suspects) if (suspect) { if (!IsPedOccupiedbyLSPDFRInteraction(suspect)) { suspect.Tasks.Clear(); suspect.Dismiss(); GameFiber.Sleep(6000); } else { suspect.IsPersistent = false; } }
            for (int i = 1; i < Units[0].UnitOfficers.Count; i++) { Units[0].UnitOfficers[i].Tasks.Clear(); }
            if (Units[0].UnitOfficers.Count != 1)
            {
                var officerAtCopCar = Units[0].UnitOfficers[0].Tasks.FollowNavigationMeshToPosition(Units[0].PoliceVehicle.GetOffsetPosition(new Vector3(2.7f, 0f, 0f)), heading + 0f, 0.75f);
                Units[0].UnitOfficers[1].Tasks.FollowNavigationMeshToPosition(Units[0].PoliceVehicle.GetOffsetPosition(new Vector3(2.2f, 1.3f, 0f)), heading + 180f, 0.75f).WaitForCompletion();
                while (officerAtCopCar.IsActive) { GameFiber.Sleep(300); }
                Rage.Native.NativeFunction.Natives.x5AD23D40115353AC(Units[0].UnitOfficers[0], Units[0].UnitOfficers[1], 0);      //TASK_TURN_PED_TO_FACE_ENTITY
                Rage.Native.NativeFunction.Natives.x5AD23D40115353AC(Units[0].UnitOfficers[1], Units[0].UnitOfficers[0], 0);      //TASK_TURN_PED_TO_FACE_ENTITY
                GameFiber.Sleep(16000);
            }

            finished = true;
            EnterAndDismiss(Units[0]);
        }
        private void NothingHappens()
        {
            LogTrivialDebug_withAiC(" DEBUG: NothingHappens() entered");
            int a = 0; while (a < 46/*seconds*/) if (!Game.IsPaused && !Rage.Native.NativeFunction.Natives.IS_PAUSE_MENU_ACTIVE<bool>()) { a++; GameFiber.Sleep(1000); }        //sleep wait for action until player is back in game
            Rage.Native.NativeFunction.Natives.x5AD23D40115353AC(Units[0].UnitOfficers[0], Game.LocalPlayer.Character, 0);      //TASK_TURN_PED_TO_FACE_ENTITY
            GameFiber.Sleep(1000);
            Units[0].UnitOfficers[1].PlayAmbientSpeech(null, "GENERIC_THANKS", 0, SpeechModifier.Force); 
            Game.DisplaySubtitle("~b~Officer~w~: We are done. Thanks for your help.", 3000);
            GameFiber.Sleep(3000);


            //Peds Leave, Cops Aproach own vehicle
            Game.LogTrivialDebug($"[AmbientAICallouts] [AiCallout MVA] DEBUG: Scene cleared");
            Functions.AiCandHA_DismissHelicopter(MO);
            foreach (var suspect in Suspects) if (suspect) { suspect.Tasks.Clear(); suspect.Dismiss(); GameFiber.Sleep(6000); }
            for (int i = 1; i < Units[0].UnitOfficers.Count; i++) { Units[0].UnitOfficers[i].Tasks.Clear(); }
            if (Units[0].UnitOfficers.Count != 1)
            {
                var officerAtCopCar = Units[0].UnitOfficers[0].Tasks.FollowNavigationMeshToPosition(Units[0].PoliceVehicle.GetOffsetPosition(new Vector3(2.7f, 0f, 0f)), heading + 0f, 0.75f);
                Units[0].UnitOfficers[1].Tasks.FollowNavigationMeshToPosition(Units[0].PoliceVehicle.GetOffsetPosition(new Vector3(2.2f, 1.3f, 0f)), heading + 180f, 0.75f).WaitForCompletion();
                while (officerAtCopCar.IsActive) { GameFiber.Sleep(300); }
                Rage.Native.NativeFunction.Natives.x5AD23D40115353AC(Units[0].UnitOfficers[0], Units[0].UnitOfficers[1], 0);      //TASK_TURN_PED_TO_FACE_ENTITY
                Rage.Native.NativeFunction.Natives.x5AD23D40115353AC(Units[0].UnitOfficers[1], Units[0].UnitOfficers[0], 0);      //TASK_TURN_PED_TO_FACE_ENTITY
                GameFiber.Sleep(16000);
            }

            finished = true;
            EnterAndDismiss(Units[0]);
        }

        private bool IsPedOccupiedbyLSPDFRInteraction(Ped ped)
        {
            if (
                   LSPDFR_Functions.IsPedArrested(ped)
                || LSPDFR_Functions.IsPedBeingCuffed(ped)
                || LSPDFR_Functions.IsPedBeingFrisked(ped)
                || LSPDFR_Functions.IsPedBeingGrabbed(ped)
                || LSPDFR_Functions.IsPedInPursuit(ped)
                )
            {
                return true;
            }
            else
            {
                return false;
            }
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


        internal void Events_patDownPedEvent(Ped ped)
        {
            if (ped == Suspects[1])
            {
                suspectFirskedOverSTP = true;
                StopThePed.API.Functions.injectPedSearchItems(Suspects[1]);
            }
        }

        internal void Events_pedArrestedEvent(Ped ped)
        {
            if (ped == Suspects[1])
            {
                suspectArrestedOverSTP = true;
            }
        }
    }

    internal class ExternalPluginSupport
    {
        //STP
        internal static bool isPedBeeingGrabbedBySTP(Ped ped)
        {
            if (StopThePed.API.Functions.isPedGrabbed(ped)) return true; else return false;
        }

        internal static void logInEvents(MVA mva)
        {
            StopThePed.API.Events.patDownPedEvent += mva.Events_patDownPedEvent;
            StopThePed.API.Events.pedArrestedEvent += mva.Events_pedArrestedEvent;
        }

        internal static void logOutEvents(MVA mva)
        {
            StopThePed.API.Events.patDownPedEvent -= mva.Events_patDownPedEvent;
            StopThePed.API.Events.pedArrestedEvent -= mva.Events_pedArrestedEvent;
        }
    }
}