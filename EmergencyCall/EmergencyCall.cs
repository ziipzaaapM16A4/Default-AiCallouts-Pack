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

                Vector3 roadside = new Vector3();
                Vector3 streetDirection = new Vector3();
                bool posFound = false;
                int trys = 0;
                bool demandPavement = true;
                while (!posFound)
                {
                    roadside = World.GetNextPositionOnStreet(Game.LocalPlayer.Character.Position.Around(AmbientAICallouts.API.Functions.minimumAiCalloutDistance + 10f, AmbientAICallouts.API.Functions.maximumAiCalloutDistance - 10f));
                    //Vector3 irrelevant;
                    //float heading = 0f;       //vieleicht guckt der MVA dann in fahrtrichtung der unit


                    //NativeFunction.Natives.xA0F8A7517A273C05<bool>(roadside.X, roadside.Y, roadside.Z, heading, out roadside); //_GET_ROAD_SIDE_POINT_WITH_HEADING
                    //NativeFunction.Natives.xFF071FB798B803B0<bool>(roadside.X, roadside.Y, roadside.Z, out irrelevant, out heading, 0, 3.0f, 0f); //GET_CLOSEST_VEHICLE_NODE_WITH_HEADING //Find Side of the road.

                    NativeFunction.Natives.xB61C8E878A4199CA<bool>(roadside, demandPavement, out roadside, 16); //GET_SAFE_COORD_FOR_PED
                    Location = roadside;


                    if (Functions.IsLocationAcceptedBySystem(Location))
                        posFound = true;

                    trys++;
                    if (trys == 30) { demandPavement = false; }
                    if (trys >= 60) { LogTrivial_withAiC("ERROR: in AICallout object: At Setup(): unable to find safe coords for this event"); return false; }
                }

                caller = new Ped(Location);
                Rage.Native.NativeFunction.Natives.x240A18690AE96513<bool>(caller.Position, out streetDirection, 0, 3f, 0f); //GET_CLOSEST_VEHICLE_NODE
                Helper.TurnPedToFace(caller, streetDirection);
                GameFiber.Sleep(2500);
                caller.Tasks.PlayAnimation(new AnimationDictionary("oddjobs@towingangryidle_a"), "idle_c", 2f, AnimationFlags.Loop);

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
                if (!IsUnitInTime(Units[0], 100f, ResponseType == EResponseType.Code3 ? 130 : 260))  //if vehicle is never reaching its location                                                          //loger so that player can react
                {
                    Disregard();
                }
                else
                {
                    OfficersArrive();
                    Helper.PedsAproachAndFaceUntilReached(Units[0].UnitOfficers, caller.Position, 1f, 4f);


                    foreach (var ofc in Units[0].UnitOfficers) { Helper.TurnPedToFace(ofc, caller); }
                    Helper.TurnPedToFace(caller, Units[0].UnitOfficers[0]);
                    GameFiber.Sleep(2000);

                    NativeFunction.Natives.x142A02425FF02BD9(Units[0].UnitOfficers[0], "CODE_HUMAN_MEDIC_TIME_OF_DEATH", 40000, true);  //TASK_START_SCENARIO_IN_PLACE
                    NativeFunction.Natives.x142A02425FF02BD9(Units[0].UnitOfficers[1], "CODE_HUMAN_POLICE_INVESTIGATE", 40000, true);  //TASK_START_SCENARIO_IN_PLACE
                    var callerAnimation = caller.Tasks.PlayAnimation(new AnimationDictionary("oddjobs@towingangryidle_a"), "idle_c", 2f, AnimationFlags.Loop);

                    GameFiber.SleepWhile(() => Units[0].UnitOfficers.Any( ofc => Helper.IsTaskActive(ofc, 118)), 60000);
                    caller.Tasks.Clear();
                    //caller.Tasks.Wander();
                    caller.Dismiss();

                    if (Units[0].UnitOfficers.Count > 1)
                    {
                        GameFiber.Sleep(1800);
                        Helper.TurnPedToFace(Units[0].UnitOfficers[0], Units[0].UnitOfficers[1]);
                        Helper.TurnPedToFace(Units[0].UnitOfficers[1], Units[0].UnitOfficers[0]);
                        GameFiber.Sleep(3000);
                        Rage.Native.NativeFunction.Natives.x142A02425FF02BD9(Units[0].UnitOfficers[0], "WORLD_HUMAN_HANG_OUT_STREET", 10000, true); //TASK_START_SCENARIO_IN_PLACE
                        Rage.Native.NativeFunction.Natives.x142A02425FF02BD9(Units[0].UnitOfficers[1], "WORLD_HUMAN_HANG_OUT_STREET", 10000, true); //TASK_START_SCENARIO_IN_PLACE
                        GameFiber.Sleep(10000);
                    }

                }
                return true;
            }
            catch (System.Threading.ThreadAbortException) { if (caller) caller.Delete(); return false; }
            catch (Exception e)
            {
                if (caller) caller.Delete();
                LogTrivial_withAiC("ERROR: in AICallout object: At Process(): " + e);
                return false;
            }
        }

        private void OfficersArrive()
        {
            GameFiber.WaitWhile(() => Units[0].PoliceVehicle.Position.DistanceTo(Location) >= 40f, 25000);
            Units[0].PoliceVehicle.IsSirenSilent = true;
            Units[0].PoliceVehicle.TopSpeed = 12f;
            OfficerReportOnScene(Units[0]);

            GameFiber.SleepUntil(() => Location.DistanceTo(Units[0].PoliceVehicle.Position) < arrivalDistanceThreshold + 5f /* && Units[0].PoliceVehicle.Speed <= 1*/, 30000);
            Units[0].PoliceVehicle.Driver.Tasks.PerformDrivingManeuver(VehicleManeuver.Wait);
            GameFiber.SleepUntil(() => Units[0].PoliceVehicle.Speed <= 1, 5000);
            OfficersLeaveVehicle(Units[0], false);
        }

        public override bool End()
        {
            try
            {
                if (caller) caller.IsPersistent = false;
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