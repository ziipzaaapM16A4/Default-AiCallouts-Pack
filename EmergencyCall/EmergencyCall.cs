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
        Ped caller;
        public override bool Setup()
        {
            try
            {
                SceneInfo = "Civilian in need of assistance";
                Vector3 roadside = World.GetNextPositionOnStreet(Unit.Position.Around2D(Functions.minimumAiCalloutDistance, Functions.maximumAiCalloutDistance));

                Vector3 irrelevant;
                float heading = 0f;       //vieleicht guckt der MVA dann in fahrtrichtung der unit

                NativeFunction.Natives.x240A18690AE96513<bool>(roadside.X, roadside.Y, roadside.Z, out roadside, 0, 3.0f, 0f);//GET_CLOSEST_VEHICLE_NODE

                NativeFunction.Natives.xA0F8A7517A273C05<bool>(roadside.X, roadside.Y, roadside.Z, heading, out roadside); //_GET_ROAD_SIDE_POINT_WITH_HEADING
                NativeFunction.Natives.xFF071FB798B803B0<bool>(roadside.X, roadside.Y, roadside.Z, out irrelevant, out heading, 0, 3.0f, 0f); //GET_CLOSEST_VEHICLE_NODE_WITH_HEADING //Find Side of the road.

                location = roadside;
                calloutDetailsString = "CIV_ASSISTANCE";

                caller = new Ped(location);
                return true;
            }
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
                if (!IsUnitInTime(100f, 130))  //if vehicle is never reaching its location                                                          //loger so that player can react
                {
                    Disregard();
                }
                else
                {
                    bool startupFinished = false;
                    GameFiber.StartNew(delegate
                    { 
                        while (Game.LocalPlayer.Character.Position.DistanceTo(caller) > 26f && !startupFinished)
                        {
                            GameFiber.Sleep(200);
                        }
                        if (!startupFinished)
                        {
                            caller.Tasks.PlayAnimation(new AnimationDictionary("oddjobs@towingangryidle_a"), "idle_c", 2f, AnimationFlags.Loop);
                            for (int i = 1; i < UnitOfficers.Count; i++) { UnitOfficers[i].Tasks.PlayAnimation(new AnimationDictionary("amb@code_human_wander_idles_cop@male@static"), "static", 1f, AnimationFlags.Loop); }
                        }
                    });

                    OfficersAproach();
                    startupFinished = true;

                    NativeFunction.Natives.x5AD23D40115353AC(UnitOfficers[0], caller, 0);      //TASK_TURN_PED_TO_FACE_ENTITY
                    NativeFunction.Natives.x5AD23D40115353AC(UnitOfficers[1], caller, 0);      //TASK_TURN_PED_TO_FACE_ENTITY
                    NativeFunction.Natives.x5AD23D40115353AC(caller, UnitOfficers[0], 0);      //TASK_TURN_PED_TO_FACE_ENTITY
                    GameFiber.Sleep(1000);

                    var callerAnimation = caller.Tasks.PlayAnimation(new AnimationDictionary("oddjobs@towingangryidle_a"), "idle_c", 2f, AnimationFlags.Loop);
                    for (int i = 1; i < UnitOfficers.Count; i++) { UnitOfficers[i].Tasks.PlayAnimation(new AnimationDictionary("amb@code_human_wander_idles_cop@male@static"), "static", 1f, AnimationFlags.Loop); }
                    var notebookAnimationFinished = false;
                    GameFiber.StartNew(delegate
                    {
                        try
                        {
                            var taskPullsOutNotebook = UnitOfficers[0].Tasks.PlayAnimation(new AnimationDictionary("amb@medic@standing@timeofdeath@enter"), "enter", 2f, AnimationFlags.None);
                            GameFiber.SleepUntil(() => taskPullsOutNotebook.CurrentTimeRatio > 0.92f, 10000);
                            UnitOfficers[0].Tasks.PlayAnimation(new AnimationDictionary("amb@medic@standing@timeofdeath@base"), "base", 2f, AnimationFlags.Loop);
                            GameFiber.Sleep(4000);

                            var watchClock = UnitOfficers[0].Tasks.PlayAnimation(new AnimationDictionary("amb@medic@standing@timeofdeath@idle_a"), "idle_b", 2f, AnimationFlags.None);
                            GameFiber.SleepUntil(() => watchClock.CurrentTimeRatio > 0.92f, 10000);

                            UnitOfficers[0].Tasks.PlayAnimation(new AnimationDictionary("amb@medic@standing@timeofdeath@base"), "base", 2f, AnimationFlags.Loop);
                            GameFiber.Sleep(2500);

                            var looksAround = UnitOfficers[0].Tasks.PlayAnimation(new AnimationDictionary("amb@medic@standing@timeofdeath@idle_a"), "idle_c", 2f, AnimationFlags.None);
                            GameFiber.SleepUntil(() => looksAround.CurrentTimeRatio > 0.92f, 10000);

                            UnitOfficers[0].Tasks.PlayAnimation(new AnimationDictionary("amb@medic@standing@timeofdeath@base"), "base", 2f, AnimationFlags.Loop);
                            GameFiber.Sleep(3000);
                            caller.Tasks.Clear();
                            GameFiber.Sleep(1000);

                            var putNotebookBack = UnitOfficers[0].Tasks.PlayAnimation(new AnimationDictionary("amb@medic@standing@timeofdeath@exit"), "exit", 2f, AnimationFlags.None);
                            GameFiber.SleepUntil(() => !putNotebookBack.IsActive, 10000);
                            UnitOfficers[0].Tasks.Clear();
                            for (int i = 1; i < UnitOfficers.Count; i++) { UnitOfficers[i].Tasks.Clear(); }
                            notebookAnimationFinished = true;
                        } catch { }
                    }, "UnitOfficer Animation Fiber");

                    while (!notebookAnimationFinished) { GameFiber.Sleep(1000); }
                    caller.Tasks.Wander();
                    caller.Dismiss();

                    if (UnitOfficers.Count > 1)
                    {
                        GameFiber.Sleep(1800);
                        NativeFunction.Natives.x5AD23D40115353AC(UnitOfficers[0], UnitOfficers[1], 0);      //TASK_TURN_PED_TO_FACE_ENTITY
                        NativeFunction.Natives.x5AD23D40115353AC(UnitOfficers[1], UnitOfficers[0], 0);      //TASK_TURN_PED_TO_FACE_ENTITY
                        GameFiber.Sleep(8000);
                    }
                    
                }
                return true;
            }
            catch (Exception e)
            {
                LogTrivial_withAiC("ERROR: in AICallout object: At Process(): " + e);
                return false;
            }
        }

        private void OfficersAproach()
        {
            GameFiber.WaitWhile(() => Unit.Position.DistanceTo(location) >= 40f, 0);
            Unit.IsSirenSilent = true;
            Unit.TopSpeed = 12f;

            GameFiber.SleepUntil(() => location.DistanceTo(Unit.Position) < arrivalDistanceThreshold + 5f /* && Unit.Speed <= 1*/, 30000);
            Unit.Driver.Tasks.PerformDrivingManeuver(VehicleManeuver.Wait);
            GameFiber.SleepUntil(() => Unit.Speed <= 1, 5000);
            OfficersLeaveVehicle(false);

            while (UnitOfficers[0].DistanceTo(caller) > 5f || (UnitOfficers.Count > 1 ? UnitOfficers[1].DistanceTo(caller) > 5f : true))
            {
                foreach (var officer in UnitOfficers)
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
                EnterAndDismiss();
                return true;
            }
            catch (Exception e)
            {
                LogTrivial_withAiC("ERROR: in AICallout object: At End(): " + e);
                return false;
            }
        }
    }
}