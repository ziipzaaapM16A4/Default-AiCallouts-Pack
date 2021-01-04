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
                int i = 0;
                while (!posFound && i < 50)
                {
                    location = World.GetNextPositionOnStreet(Unit.Position.Around(AmbientAICallouts.API.Functions.minimumAiCalloutDistance + 10f, AmbientAICallouts.API.Functions.maximumAiCalloutDistance - 10f));
                    if (Unit.Position.DistanceTo(location) > Functions.minimumAiCalloutDistance
                     && Unit.Position.DistanceTo(location) < Functions.maximumAiCalloutDistance)
                        posFound = true;
                    i++;
                }
                calloutDetailsString = "CIV_ASSISTANCE";
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
                if (!IsUnitInTime(100f, 130))  //if vehicle is never reaching its location                                                          //loger so that player can react
                {
                    Disregard();
                }
                else
                {
                    GameFiber.WaitWhile(() => Unit.Position.DistanceTo(location) >= 40f, 0);
                    Unit.IsSirenSilent = true;
                    Unit.TopSpeed = 12f;

                    GameFiber.SleepUntil(() => location.DistanceTo(Unit.Position) < arrivalDistanceThreshold + 5f /* && Unit.Speed <= 1*/, 30000);
                    Unit.Driver.Tasks.PerformDrivingManeuver(VehicleManeuver.Wait);
                    GameFiber.SleepUntil(() => Unit.Speed <= 1, 5000);
                    OfficersLeaveVehicle(true);

                    LogTrivialDebug_withAiC($"DEBUG: Go Look Around");
                    string[] anims = { "wait_idle_a", "wait_idle_a", "wait_idle_a" };
                    foreach (var officer in UnitOfficers) { officer.Tasks.FollowNavigationMeshToPosition(location.Around(7f, 10f), Unit.Heading, 0.6f, 20f, 20000); }                       //ToHeading is useless
                    GameFiber.Sleep(12000);                                                                                                   //Static behavior. bad way                                   
                    for (int i = 0; i < UnitOfficers.Count; i++)
                    {
                        if (UnitOfficers[i]) UnitOfficers[i].Tasks.PlayAnimation(new AnimationDictionary("missmic_4premierejimwaitbef_prem"), anims[randomizer.Next(0, anims.Length)], 1f, AnimationFlags.RagdollOnCollision);
                        GameFiber.Sleep(2000);
                    }
                    GameFiber.SleepWhile(() => UnitOfficers[0].Tasks.CurrentTaskStatus == Rage.TaskStatus.InProgress || UnitOfficers[0].Tasks.CurrentTaskStatus == Rage.TaskStatus.Preparing, 7000);

                    LogTrivialDebug_withAiC($"DEBUG: PrankCallSpeech");
                    UnitOfficers[0].PlayAmbientSpeech("S_M_Y_FIREMAN_01_WHITE_FULL_01", "EMERG_PRANK_CALL", 0, SpeechModifier.Force);                                                                       //Not finished needs speech
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
                EnterAndDismiss();
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