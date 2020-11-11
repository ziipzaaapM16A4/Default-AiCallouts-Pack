using System;
using Rage;
using AmbientAICallouts.API;
using System.CodeDom;

namespace Debug_AiC
{
    //Its very important to use these unless the AiCallout will not be compatible with the player callouts
    //Please use try catch blocks to get an error message when chrashing.
    ///internal Vector3 location;
    ///internal String callSign;
    ///internal String calloutDetailsString;
    ///internal Voicelines rndVl;
    ///internal Vehicle unit;
    ///internal List<Ped> unitOfficers = new List<Ped>();
    ///internal List<Ped> suspects = new List<Ped>();
    ///internal List<Vehicle> suspectsVehicle = new List<Vehicle>();

    ///For almost every API Function you will need an so called "functions Object". It knows to current managerObject which contains most of the features in AiCalloutManager. The object name is always "fO"
    //-------------------------->>> Important: Always use fO when calling functions to ensure you are using the right managerObject <<<-------------------------

    public class Debug_AiC : AiCallout
    {
        public override bool Setup()
        {
            //Code for setting the scene. return true when Succesfull. 
            //Important: please set a calloutDetailsString with Set_AiCallout_calloutDetailsString(String calloutDetailsString) to ensure that your callout has a something a civilian can report.
            //Example idea: Place a Damaged Vehicle infront of a Pole and place a swearing ped nearby.
            try
            {
                SceneInfo = "debug";
                Game.DisplayNotification("DEBUG AiCallout Starting");
                location = World.GetNextPositionOnStreet(Game.LocalPlayer.Character.Position.Around2D(4f,6f));
                arrivalDistanceThreshold = 10f;
                LogTrivial_withAiC("DEBUG MSG: get arrivalDistanceThreshold = " + arrivalDistanceThreshold);
                calloutDetailsString = "EMERGENCY_CALL";
                SetupSuspects(1);
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
            //Code for processing the the scene. return true when Succesfull.
            //Example idea: Cops arrive; Getting out; Starring at suspects; End();
            try
            {
                if (!IsUnitInTime(100f, 130))  //if vehicle is never reaching its location
                {
                    Disregard();
                }
                else  //if vehicle is reaching its location
                {
                    GameFiber.WaitWhile(() => Unit.Position.DistanceTo(location) >= 40f, 0);
                    Unit.IsSirenSilent = true;
                    Unit.TopSpeed = 12f;

                    GameFiber.SleepUntil(() => location.DistanceTo(Unit.Position) < arrivalDistanceThreshold + 5f /* && Unit.Speed <= 1*/, 30000);
                    Unit.Driver.Tasks.PerformDrivingManeuver(VehicleManeuver.Wait);
                    GameFiber.SleepUntil(() => Unit.Speed <= 1, 5000);
                    OfficersLeaveVehicle(true);
                    foreach (var officer in UnitOfficers)
                    {
                        officer.Tasks.FollowNavigationMeshToPosition(Suspects[0].Position, MathHelper.ConvertDirectionToHeading(Suspects[0].Position), 1f);
                    }
                    GameFiber.Sleep(2500);
                    UnitCallsForBackup("AAIC-OfficerDown");

                    while (LSPD_First_Response.Mod.API.Functions.IsCalloutRunning()) { GameFiber.Sleep(4000); }
                }

                return true;
            }
            catch (Exception e)
            {
                LogTrivial_withAiC("ERROR: in AICallout object: At Process(): " + e);
                return false;
            }
        }

        public override bool End()
        {
            //Code for finishing the the scene. return true when Succesfull.
            //Example idea: Cops getting back into their vehicle. drive away dismiss the rest. after 90 secconds delete if possible entitys that have not moved away.
            try
            {

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