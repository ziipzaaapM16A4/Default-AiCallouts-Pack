using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Rage;
using Rage.Native;
using AmbientAICallouts.API;

namespace EmergencyCall
{
    public class EmergencyCall : AiCallout
    {
        private Ped caller;
        private Rage.Object notepad = null;
        private Random rand = new Random();
        public override bool Setup()
        {
            try
            {
                SceneInfo = "Civilian in need of assistance";
                CalloutDetailsString = "CIV_ASSISTANCE";
                if (rand.Next(0, 2) == 0) { ResponseType = EResponseType.Code3; } else { ResponseType = EResponseType.Code2; }

                Vector3 roadside = World.GetNextPositionOnStreet(Game.LocalPlayer.Character.Position.Around(AmbientAICallouts.API.Functions.minimumAiCalloutDistance + 10f, AmbientAICallouts.API.Functions.maximumAiCalloutDistance - 10f));
                bool posFound = false;
                int trys = 0;
                while (!posFound && trys < 30)
                {
                    roadside = World.GetNextPositionOnStreet(Game.LocalPlayer.Character.Position.Around(AmbientAICallouts.API.Functions.minimumAiCalloutDistance + 10f, AmbientAICallouts.API.Functions.maximumAiCalloutDistance - 10f));
                    Vector3 irrelevant;
                    float heading = 0f;       //vieleicht guckt der MVA dann in fahrtrichtung der unit

                    NativeFunction.Natives.x240A18690AE96513<bool>(roadside.X, roadside.Y, roadside.Z, out roadside, 0, 3.0f, 0f);//GET_CLOSEST_VEHICLE_NODE

                    NativeFunction.Natives.xA0F8A7517A273C05<bool>(roadside.X, roadside.Y, roadside.Z, heading, out roadside); //_GET_ROAD_SIDE_POINT_WITH_HEADING
                    NativeFunction.Natives.xFF071FB798B803B0<bool>(roadside.X, roadside.Y, roadside.Z, out irrelevant, out heading, 0, 3.0f, 0f); //GET_CLOSEST_VEHICLE_NODE_WITH_HEADING //Find Side of the road.

                    Location = roadside;


                    if (Location.DistanceTo(Game.LocalPlayer.Character.Position) > AmbientAICallouts.API.Functions.minimumAiCalloutDistance
                     && Location.DistanceTo(Game.LocalPlayer.Character.Position) < AmbientAICallouts.API.Functions.maximumAiCalloutDistance)
                        posFound = true;
                    trys++;
                }

                caller = new Ped(Location);
                return true;
            }
            catch (System.Threading.ThreadAbortException) { return false; }
            catch (Exception e)
            {
                LogTrivial_withAiC("ERROR: in AICallout object: At Setup(): " + e);
                return false;
            }
        }
        public override bool Process()
        {
            try
            {
                if (!IsUnitInTime(Units[0].PoliceVehicle, 100f, ResponseType == EResponseType.Code3 ? 130 : 260))  //if vehicle is never reaching its location                                                          //loger so that player can react
                {
                    Disregard();
                }
                else
                {
                    bool startupFinished = false;
                    GameFiber.StartNew(delegate
                    {
                        try
                        {
                            while (Game.LocalPlayer.Character.Position.DistanceTo(caller) > 26f && !startupFinished)
                            {
                                GameFiber.Sleep(200);
                            }
                            if (!startupFinished)
                            {
                                caller.Tasks.PlayAnimation(new AnimationDictionary("oddjobs@towingangryidle_a"), "idle_c", 2f, AnimationFlags.Loop);
                                for (int i = 1; i < Units[0].UnitOfficers.Count; i++) { Units[0].UnitOfficers[i].Tasks.PlayAnimation(new AnimationDictionary("amb@code_human_wander_idles_cop@male@static"), "static", 1f, AnimationFlags.Loop); }
                            }
                        }
                        catch { }
                    });

                    OfficersAproach();
                    startupFinished = true;

                    NativeFunction.Natives.x5AD23D40115353AC(Units[0].UnitOfficers[0], caller, 0);      //TASK_TURN_PED_TO_FACE_ENTITY
                    NativeFunction.Natives.x5AD23D40115353AC(Units[0].UnitOfficers[1], caller, 0);      //TASK_TURN_PED_TO_FACE_ENTITY
                    NativeFunction.Natives.x5AD23D40115353AC(caller, Units[0].UnitOfficers[0], 0);      //TASK_TURN_PED_TO_FACE_ENTITY
                    GameFiber.Sleep(1000);

                    var callerAnimation = caller.Tasks.PlayAnimation(new AnimationDictionary("oddjobs@towingangryidle_a"), "idle_c", 2f, AnimationFlags.Loop);
                    for (int i = 1; i < Units[0].UnitOfficers.Count; i++) { Units[0].UnitOfficers[i].Tasks.PlayAnimation(new AnimationDictionary("amb@code_human_wander_idles_cop@male@static"), "static", 1f, AnimationFlags.Loop); }
                    var notebookAnimationFinished = false;
                    GameFiber.StartNew(delegate
                    {
                        try
                        {
                            notepad = new Rage.Object("prop_notepad_02", Units[0].UnitOfficers[0].Position, 0f);
                            notepad.AttachTo(Units[0].UnitOfficers[0], NativeFunction.Natives.GET_PED_BONE_INDEX<int>(Units[0].UnitOfficers[0], 18905), new Vector3(0.16f, 0.05f, -0.01f), new Rotator(-37f, -19f, .32f));
                            var taskPullsOutNotebook = Units[0].UnitOfficers[0].Tasks.PlayAnimation(new AnimationDictionary("amb@medic@standing@timeofdeath@enter"), "enter", 2f, AnimationFlags.None);
                            GameFiber.SleepUntil(() => taskPullsOutNotebook.CurrentTimeRatio > 0.92f, 10000);
                            Units[0].UnitOfficers[0].Tasks.PlayAnimation(new AnimationDictionary("amb@medic@standing@timeofdeath@base"), "base", 2f, AnimationFlags.Loop);
                            GameFiber.Sleep(4000);

                            var watchClock = Units[0].UnitOfficers[0].Tasks.PlayAnimation(new AnimationDictionary("amb@medic@standing@timeofdeath@idle_a"), "idle_b", 2f, AnimationFlags.None);
                            GameFiber.SleepUntil(() => watchClock.CurrentTimeRatio > 0.92f, 10000);

                            Units[0].UnitOfficers[0].Tasks.PlayAnimation(new AnimationDictionary("amb@medic@standing@timeofdeath@base"), "base", 2f, AnimationFlags.Loop);
                            GameFiber.Sleep(2500);

                            var looksAround = Units[0].UnitOfficers[0].Tasks.PlayAnimation(new AnimationDictionary("amb@medic@standing@timeofdeath@idle_a"), "idle_c", 2f, AnimationFlags.None);
                            GameFiber.SleepUntil(() => looksAround.CurrentTimeRatio > 0.92f, 10000);

                            Units[0].UnitOfficers[0].Tasks.PlayAnimation(new AnimationDictionary("amb@medic@standing@timeofdeath@base"), "base", 2f, AnimationFlags.Loop);
                            GameFiber.Sleep(3000);
                            caller.Tasks.Clear();
                            GameFiber.Sleep(1000);

                            var putNotebookBack = Units[0].UnitOfficers[0].Tasks.PlayAnimation(new AnimationDictionary("amb@medic@standing@timeofdeath@exit"), "exit", 2f, AnimationFlags.None);
                            GameFiber.SleepUntil(() => !putNotebookBack.IsActive, 10000);
                            if (notepad) notepad.Delete();
                            Units[0].UnitOfficers[0].Tasks.Clear();
                            for (int i = 1; i < Units[0].UnitOfficers.Count; i++) { Units[0].UnitOfficers[i].Tasks.Clear(); }
                            notebookAnimationFinished = true;
                        } catch { }
                    }, "UnitOfficer Animation Fiber");

                    while (!notebookAnimationFinished) { GameFiber.Sleep(1000); }
                    caller.Tasks.Wander();
                    caller.Dismiss();

                    if (Units[0].UnitOfficers.Count > 1)
                    {
                        GameFiber.Sleep(1800);
                        NativeFunction.Natives.x5AD23D40115353AC(Units[0].UnitOfficers[0], Units[0].UnitOfficers[1], 0);      //TASK_TURN_PED_TO_FACE_ENTITY
                        NativeFunction.Natives.x5AD23D40115353AC(Units[0].UnitOfficers[1], Units[0].UnitOfficers[0], 0);      //TASK_TURN_PED_TO_FACE_ENTITY
                        GameFiber.Sleep(8000);
                    }
                    
                }
                return true;
            }
            catch (System.Threading.ThreadAbortException) { return false; }
            catch (Exception e)
            {
                LogTrivial_withAiC("ERROR: in AICallout object: At Process(): " + e);
                return false;
            }
        }

        private void OfficersAproach()
        {
            GameFiber.WaitWhile(() => Units[0].PoliceVehicle.Position.DistanceTo(Location) >= 40f, 25000);
            Units[0].PoliceVehicle.IsSirenSilent = true;
            Units[0].PoliceVehicle.TopSpeed = 12f;
            OfficerReportOnScene(Units[0]);

            GameFiber.SleepUntil(() => Location.DistanceTo(Units[0].PoliceVehicle.Position) < arrivalDistanceThreshold + 5f /* && Units[0].PoliceVehicle.Speed <= 1*/, 30000);
            Units[0].PoliceVehicle.Driver.Tasks.PerformDrivingManeuver(VehicleManeuver.Wait);
            GameFiber.SleepUntil(() => Units[0].PoliceVehicle.Speed <= 1, 5000);
            OfficersLeaveVehicle(Units[0],false);

            while (Units[0].UnitOfficers[0].DistanceTo(caller) > 5f || (Units[0].UnitOfficers.Count > 1 ? Units[0].UnitOfficers[1].DistanceTo(caller) > 5f : true))
            {
                foreach (var officer in Units[0].UnitOfficers)
                {
                    officer.Tasks.FollowNavigationMeshToPosition(caller.Position, MathHelper.ConvertDirectionToHeading(caller.Position), 1f, 3.5f, 15000);
                }
                GameFiber.Sleep(800);
            }
        }

        public override bool End()
        {
            try
            {
                EnterAndDismiss(Units[0]);
                return true;
            }
            catch (System.Threading.ThreadAbortException) { return false; }
            catch (Exception e)
            {
                LogTrivial_withAiC("ERROR: in AICallout object: At End(): " + e);
                return false;
            }
        }
    }
}