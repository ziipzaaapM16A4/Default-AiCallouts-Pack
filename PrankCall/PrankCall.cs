using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Rage;
using AmbientAICallouts.API;

namespace PrankCall
{
    public class PrankCall : AiCallout
    {
        Random randomizer = new Random();
        public override bool Setup()
        {
            try
            {
                SceneInfo = "Civilian in need of assistance";
                bool posFound = false;
                int trys = 0;
                while (!posFound && trys < 20)
                {
                    Location = World.GetNextPositionOnStreet(Game.LocalPlayer.Character.Position.Around(AmbientAICallouts.API.Functions.minimumAiCalloutDistance + 10f, AmbientAICallouts.API.Functions.maximumAiCalloutDistance - 10f));
                    if (Location.DistanceTo(Game.LocalPlayer.Character.Position) > AmbientAICallouts.API.Functions.minimumAiCalloutDistance
                     && Location.DistanceTo(Game.LocalPlayer.Character.Position) < AmbientAICallouts.API.Functions.maximumAiCalloutDistance)
                        posFound = true;
                    trys++;
                }
                CalloutDetailsString = "CIV_ASSISTANCE";
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
                if (!IsUnitInTime(Units[0], 100f, 130))  //if vehicle is never reaching its location                                                          //loger so that player can react
                {
                    Disregard();
                }
                else
                {
                    GameFiber.WaitWhile(() => Units[0].PoliceVehicle.Position.DistanceTo(Location) >= 40f, 25000);
                    Units[0].PoliceVehicle.IsSirenSilent = true;
                    Units[0].PoliceVehicle.TopSpeed = 12f;
                    OfficerReportOnScene(Units[0]);

                    GameFiber.SleepUntil(() => Location.DistanceTo(Units[0].PoliceVehicle.Position) < arrivalDistanceThreshold + 5f /* && Units[0].PoliceVehicle.Speed <= 1*/, 30000);
                    Units[0].PoliceVehicle.Driver.Tasks.PerformDrivingManeuver(VehicleManeuver.Wait);
                    GameFiber.SleepUntil(() => Units[0].PoliceVehicle.Speed <= 1, 5000);
                    OfficersLeaveVehicle(Units[0], true);

                    LogTrivialDebug_withAiC($"DEBUG: Go Look Around");
                    string[] anims = { "wait_idle_a", "wait_idle_b", "wait_idle_c" };
                    foreach (var officer in Units[0].UnitOfficers) { officer.Tasks.FollowNavigationMeshToPosition(Location.Around(7f, 10f), Units[0].PoliceVehicle.Heading, 0.6f, 20f, 20000); }                       //ToHeading is useless
                    GameFiber.Sleep(12000);                                                                                                   //Static behavior. bad way                                   
                    for (int i = 0; i < Units[0].UnitOfficers.Count; i++)
                    {
                        if (Units[0].UnitOfficers[i]) Units[0].UnitOfficers[i].Tasks.PlayAnimation(new AnimationDictionary("missmic_4premierejimwaitbef_prem"), anims[randomizer.Next(0, anims.Length)], 1f, AnimationFlags.RagdollOnCollision);
                        GameFiber.Sleep(2000);
                    }
                    GameFiber.SleepWhile(() => Units[0].UnitOfficers[0].Tasks.CurrentTaskStatus == Rage.TaskStatus.InProgress || Units[0].UnitOfficers[0].Tasks.CurrentTaskStatus == Rage.TaskStatus.Preparing, 7000);

                    LogTrivialDebug_withAiC($"DEBUG: PrankCallSpeech");
                    Units[0].UnitOfficers[0].PlayAmbientSpeech("S_M_Y_FIREMAN_01_WHITE_FULL_01", "EMERG_PRANK_CALL", 0, SpeechModifier.Force);                                                                       //Not finished needs speech
                    GameFiber.Sleep(4000);
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