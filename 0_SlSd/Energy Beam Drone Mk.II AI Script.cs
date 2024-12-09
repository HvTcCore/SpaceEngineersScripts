//Energy Beam Drone Mk.II AI Script

//Configuration
double noPlayerDespawnDist = 20000;

//Positions
Vector3D closestPlayer = new Vector3D(0,0,0);
Vector3D dronePosition = new Vector3D(0,0,0);
Vector3D originPosition = new Vector3D(0,0,0);
Vector3D planetLocation = new Vector3D(0,0,0);
Vector3D alignmentPosition = new Vector3D(0,0,0);
Vector3D currentTarget = new Vector3D(0,0,0);

//Distances
double distanceDroneToPlayer = 0;
double distanceDroneToOrigin = 0;
double distancePlayerToOrigin = 0;

//Bool Checks
bool droneIsNPC = false;
bool inNaturalGravity = false;

//Global Blocks List & Remote Control
List<IMyTerminalBlock> blockList = new List<IMyTerminalBlock>();
List<IMyGyro> gyroList = new List<IMyGyro>();
List<IMyBeacon> beaconList = new List<IMyBeacon>();
IMyRemoteControl remoteControl;

string currentMode = "Charging";
bool autoRotate = false;
int tickIncrement = 10;
int tickCounter = 0;
int targetingTimeout = 0;
Random rnd = new Random();

bool droneVulnerable = true;
string lastMessageSent = "Default";
bool greetingChat = false;

int totalLaserStrikes = 0;
bool scriptInit = false;
bool fireLaser = false;
IMyCameraBlock laserCamera = null;
int laserDamageTimer = 0;
int laserFireTimer = 0;
int chargeLaserTimer = 0;
bool laserReady = false;
bool laserDamaged = false;
long targetId = 0;

public Program(){
	
	Runtime.UpdateFrequency = UpdateFrequency.Update10;
	
}


void DroneBehaviour(string argument){
	
	Echo(currentMode);
	ShieldManagement();
	
	var shieldRegister = Me.GetValue<bool>("CorruptionShieldRegister");
	
	if(distanceDroneToPlayer < 4000 && greetingChat == false){
		
		greetingChat = true;
		TryChat("Well this puts me in a good mood. I've found some targets to test my energy cannon against!");
		
	}
	
	if(laserCamera != null && laserDamaged == false){
		
		if(laserCamera.IsFunctional == false){
			
			TryChat("You've damaged my Energy Cannon! You'll pay for that!");
			laserDamaged = true;
			currentMode = "AttackPlayerDirectly";
						
		}
		
	}else{
		
		TryChat("You've damaged my Energy Cannon! You'll pay for that!");
		laserDamaged = true;
		currentMode = "AttackPlayerDirectly";
		
	}
	
	if(currentMode == "Charging"){
		
		AutoRotateActivation(false);
		
		chargeLaserTimer++;
		
		if(totalLaserStrikes >= 5){
			
			TryChat("It's been fun tearing your property to shreds, but I've got other places to be. Until next time...");
			currentMode = "Retreat";
			return;
			
		}
		
		if(chargeLaserTimer >= 60){
			
			targetId = Me.GetValue<long>("CorruptionNearestPlayerThreat");
			
			if(targetId != 0){
				
				currentMode = "SeekTarget";
				if(laserReady == false){
					
					laserReady = true;
					TryChat("The Energy Cannon is now fully charged. Time to find something to eliminate!");
					
				}
				
			}else{
				
				
				TryChat("Doesn't look like you have much left around here. What a shame. Enjoy picking up the pieces.");
				currentMode = "Retreat";
				
			}
			
			return;
			
		}
		
		Vector3D targetCoords = new Vector3D(0,0,0);
		
		if(distanceDroneToPlayer < 1500){
			
			//Attack Player
			if(inNaturalGravity == false){
				
				targetCoords = CreateDirectionAndTarget(closestPlayer, dronePosition, closestPlayer, 250);
				
			}else{
				
				targetCoords = CreateDirectionAndTarget(planetLocation, closestPlayer, closestPlayer, 400);
				
			}
			
		}else{
			
			//Sit Outside Player Area
			if(inNaturalGravity == false){
				
				targetCoords = CreateDirectionAndTarget(closestPlayer, dronePosition, closestPlayer, 2500);
				
			}else{
				
				targetCoords = CreateDirectionAndTarget(planetLocation, closestPlayer, closestPlayer, 2500);
				
			}
			
		}
		
		SetDestination(targetCoords, false, 100);
		
	}
	
	if(currentMode == "SeekTarget"){
		
		Me.CustomData = targetId.ToString();
		currentTarget = Me.GetValue<Vector3D>("CorruptionTrackEntity");
		Echo(targetId.ToString() + "\n" + Me.CubeGrid.GetPosition().ToString() + "\n" + currentTarget.ToString());
		
		if(currentTarget == new Vector3D(0,0,0)){
			
			//No Grids Nearby
			currentMode = "Charging";
			
		}
		
		targetingTimeout++;
		
		if(targetingTimeout >= 120){
			
			targetingTimeout = 0;
			currentMode = "Charging";
			
		}
		
		if(MeasureDistance(dronePosition, currentTarget) > 1500){
			
			AutoRotateActivation(false);
			
			if(inNaturalGravity == false){
				
				currentTarget = CreateDirectionAndTarget(closestPlayer, dronePosition, currentTarget, 800);
				
			}else{
				
				currentTarget = CreateDirectionAndTarget(planetLocation, currentTarget, currentTarget, 800);
				
			}
			
			SetDestination(currentTarget, false, 100);
			
		}else{
			
			AutoRotateActivation(true);
			remoteControl.SetAutoPilotEnabled(false);
			var scanResults = CameraRayCast(remoteControl.WorldMatrix.Forward, 1500);
			
			Echo("Scan Result: " + scanResults.IsEmpty().ToString());
			if(scanResults.IsEmpty() == false){
				
				Echo("Scan Result: " + scanResults.Relationship.ToString());
				if(scanResults.Relationship == MyRelationsBetweenPlayerAndBlock.Enemies){
					
					chargeLaserTimer = 0;
					fireLaser = true;
					//Send Request For Laser Effects On Clients
					Me.CustomData = laserCamera.EntityId.ToString();
					var effectRequest = Me.GetValue<bool>("CorruptionBeamEffectRequest");
					laserReady = false;
					totalLaserStrikes++;
					TryChat("Gotcha!");
					currentMode = "FireLaser";
					return;
					
				}
				
			}
			
		}
				
	}
	
	if(currentMode == "FireLaser"){
		
		if(fireLaser == false){
			
			AutoRotateActivation(false);
			currentTarget = new Vector3D(0,0,0);
			targetId = 0;
			currentMode = "Charging";
			
			return;
			
		}
		
	}
	
	if(currentMode == "AttackPlayerDirectly"){
		
		if(distanceDroneToPlayer > 400){
			
			if(inNaturalGravity == false){
				
				CreateDirectionAndTarget(closestPlayer, dronePosition, closestPlayer, 200);
				
			}else{
				
				CreateDirectionAndTarget(planetLocation, closestPlayer, closestPlayer, 350);
				
			}
			
		}else{
			
			remoteControl.SetAutoPilotEnabled(false);
			
		}
		
	}
	
	if(currentMode == "Retreat"){
		
		var escapeCoords = new Vector3D(0,0,0);
		
		if(inNaturalGravity == false){
			
			escapeCoords =CreateDirectionAndTarget(closestPlayer, dronePosition, closestPlayer, 6000);
			
		}else{
			
			escapeCoords =CreateDirectionAndTarget(planetLocation, dronePosition, dronePosition, 6000);
			
		}
		
		SetDestination(escapeCoords, false, 100);
		
		if(distanceDroneToPlayer > 4000){
			
			TryDespawn(remoteControl, false);
			
		}
		
	}
	
}

void Main(string argument){
	
	try{
		
		if(Me.GetValue<bool>("CorruptionOwnerCheck") == false){
	
			Echo("Non-NPC");
			return;
		
		}
		
	}catch(Exception exc){
	
		//ModAPI Despawn Failed
	
	}
	
	TryRotate();
	
	if(scriptInit == false){
		
		scriptInit = true;
		laserCamera = GridTerminalSystem.GetBlockWithName("Camera (Laser)") as IMyCameraBlock;
		GridTerminalSystem.GetBlocksOfType<IMyBeacon>(beaconList);
		
		if(laserCamera != null){
			
			laserCamera.EnableRaycast = true;
			
		}
		
		ShieldManagement(true);
		
	}
		
	if(fireLaser == true){
		
		laserDamageTimer++;
		laserFireTimer++;
		LaserFireAndDamage();
		
		if(laserFireTimer >= 60){
			
			ScriptSpeedRegular();
			laserFireTimer = 0;
			laserDamageTimer = 0;
			fireLaser = false;
			
		}
		
	}
	
	tickCounter += tickIncrement;
	
	if(tickCounter < 60){
		
		return;
		
	}
	
	tickCounter = 0;
	
	//Reset Block List and Check For Remote Control
	remoteControl = GridTerminalSystem.GetBlockWithName("Remote Control") as IMyRemoteControl;
	
	inNaturalGravity = remoteControl.TryGetPlanetPosition(out planetLocation);
	
	ResetBlockList();
	
	
	//Ensure Remote Control Is NPC Owned & Get Player Location
	droneIsNPC = remoteControl.GetNearestPlayer(out closestPlayer);

	if(droneIsNPC == false){
		
		Echo("Drone is not controlled by an NPC");
		return;
		
	}
	
	OriginSetup();
		
	//Get Other Location Data
		
	dronePosition = remoteControl.GetPosition();

	//Calculate Distances
	distanceDroneToPlayer = MeasureDistance(dronePosition, closestPlayer);
	distanceDroneToOrigin = MeasureDistance(dronePosition, originPosition);
	distancePlayerToOrigin = MeasureDistance(closestPlayer, originPosition);
	
	if(distanceDroneToPlayer > noPlayerDespawnDist){
		
		TryDespawn(remoteControl, false);
		
	}
	
	if(gyroList.Count == 0){
		
		GridTerminalSystem.GetBlocksOfType<IMyGyro>(gyroList);
		
	}
	
	//Execute Custom Drone Behaviour
	DroneBehaviour(argument);
	
}

double MeasureDistance(Vector3D point_a, Vector3D point_b){
	
	double result = Math.Round( Vector3D.Distance( point_a, point_b ), 2 );
	return result;
	
}

void ChangeAntennaName(string newName){
	
	foreach(var block in blockList){
		
		if(block == null){
			
			continue;
			
		}
		
		var antenna = block as IMyRadioAntenna;
		
		if(antenna != null){
			
			antenna.CustomName = newName;
			
		}
		
	}
	
}

void ShieldManagement(bool resetShield = false){
	
	var shieldRegister = Me.GetValue<bool>("CorruptionShieldRegister");
	if(scriptInit == false){
		
		return;
		
	}
	
	bool gridVulnerable = false;
	
	if(beaconList.Count == 0){
		
		gridVulnerable = true;
		
	}else{
		
		bool noBeacons = true;
		
		foreach(var block in blockList){
			
			if(block != null){
				
				if(block.CustomName != "Shield Generator Module"){
					
					continue;
					
				}
				
				if(block.IsWorking == true){
					
					noBeacons = false;
					
				}
				
			}
			
		}
		
		gridVulnerable = noBeacons;
		
		
	}
	
	if(gridVulnerable == true && droneVulnerable == false){
			
		droneVulnerable = true;
		BlockToggle("Shield Light", false);
		Me.CustomData = droneVulnerable.ToString();
		bool result = Me.GetValue<bool>("CorruptionInvincibility");
		Me.CubeGrid.CustomName = "(NPC-CPC) Vengeance Drone";
		ChangeAntennaName("Vengeance Drone");
		TryChat("My shields are down?! Curse you!");
		
	}
	
	if(resetShield == true){
			
		droneVulnerable = true;
		BlockToggle("Shield Light", false);
		Me.CustomData = droneVulnerable.ToString();
		bool result = Me.GetValue<bool>("CorruptionInvincibility");
		Me.CubeGrid.CustomName = "(NPC-CPC) Vengeance Drone";
		ChangeAntennaName("Vengeance Drone");
		
	}
	
	if(gridVulnerable == false && droneVulnerable == true){
		
		droneVulnerable = false;
		BlockToggle("Shield Light", true);
		Me.CustomData = droneVulnerable.ToString();
		bool result = Me.GetValue<bool>("CorruptionInvincibility");
		Me.CubeGrid.CustomName = "(NPC-CPC) Shielded Vengeance Drone";
		ChangeAntennaName("Shielded Vengeance Drone");
		
	}
	
	
}

void BlockToggle(string blockName, bool toggle){
	
	foreach(var block in blockList){
		
		var fBlock = block as IMyFunctionalBlock;
		
		if(fBlock == null){
			
			continue;
			
		}
		
		if(fBlock.CustomName == blockName){
			
			fBlock.Enabled = toggle;
			
		}
		
	}
	
}

void ResetBlockList(){
	
	blockList.Clear();
	GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(blockList);
	
}

void OriginSetup(){
	
	if(droneIsNPC == false){
		
		originPosition = new Vector3D(0,0,0);
		
	}
	
	if(originPosition == new Vector3D(0,0,0) && droneIsNPC == true){
		
		originPosition = remoteControl.GetPosition();
		
	}
	
}

void SetDestination(Vector3D coords, bool collisionModeEnable, float speedLimit){
	
	remoteControl.ClearWaypoints();
	remoteControl.AddWaypoint(coords, "Destination");
	remoteControl.FlightMode = FlightMode.OneWay;
	remoteControl.SetAutoPilotEnabled(true);
	remoteControl.SetCollisionAvoidance(collisionModeEnable);
	remoteControl.SpeedLimit = speedLimit;
	
}

MyDetectedEntityInfo CameraRayCast(Vector3D direction, float scanDistance){
	
	MyDetectedEntityInfo scanResults = new MyDetectedEntityInfo();
		
	if(laserCamera == null){
		
		return scanResults;
		
	}
	
	if(laserCamera.IsFunctional == false){
		
		return scanResults;
		
	}
	
	//TODO: Figure out if camera direction is Forward or Up
	if(laserCamera.WorldMatrix.Forward != direction){
		
		return scanResults;
		
	}
	
	laserCamera.EnableRaycast = true;
			
	if(laserCamera.CanScan(scanDistance) == false){
		
		return scanResults;
		
	}
	
	scanResults = laserCamera.Raycast(scanDistance, 0, 0);	
	return scanResults;
	
}

void WeaponActivation(string weaponMode){
	
	foreach(var block in blockList){
		
		var weapon = block as IMyUserControllableGun;
		
		if(weapon == null || block.IsFunctional == false){
			
			continue;
			
		}
		
		weapon.ApplyAction(weaponMode);
		
	}
	
}

void OverrideThrust(bool enableOverride, Vector3D direction, float thrustModifier){
	
	foreach(var block in blockList){
		
		var thruster = block as IMyThrust;
		
		if(thruster == null || block.IsFunctional == false){
			
			continue;
			
		}
		
		if(enableOverride == true){
			
			if(thruster.WorldMatrix.Forward == direction * -1){
				
				thruster.Enabled = true;
				float maxthrust = thruster.MaxThrust;
				thruster.ThrustOverridePercentage = thrustModifier;
			
			}
			
			if(thruster.WorldMatrix.Forward == direction){
			
				thruster.Enabled = false;
			
			}
			
		}else{
			
			thruster.Enabled = true;
			thruster.SetValueFloat("Override", 0);
			
		}
		
	}
	
}

void ArmWarheads(bool enabled){
	
	foreach(var block in blockList){
		
		var weapon = block as IMyWarhead;
		
		if(weapon == null || block.IsFunctional == false){
			
			continue;
			
		}
		
		weapon.IsArmed = enabled;
		
	}
	
}

void SelfDestruct(string weaponMode){
	
	foreach(var block in blockList){
		
		var weapon = block as IMyWarhead;
		
		if(weapon == null || block.IsFunctional == false){
			
			continue;
			
		}
	
		if(weaponMode == "Detonate"){
			
			weapon.IsArmed = true;
			weapon.Detonate();
			
		}
		
	}
	
}

Vector3D RandomDirection(){
	
	Vector3D randomDir = new Vector3D(0,0,0);
	randomDir.X = RandomNumberBetween(-0.999999, 0.999999);
	randomDir.Y = RandomNumberBetween(-0.999999, 0.999999);
	randomDir.Z = RandomNumberBetween(-0.999999, 0.999999);
	randomDir = Vector3D.Normalize(randomDir);
	return randomDir;
	
	
}

double RandomNumberBetween(double minValue, double maxValue){
	
    var next = rnd.NextDouble();
    return minValue + (next * (maxValue - minValue));
	
}

void TryDespawn(IMyRemoteControl remoteControl, bool gravity){
	
	Vector3D planetPosition = new Vector3D(0,0,0);
	if(remoteControl.TryGetPlanetPosition(out planetPosition) == true || gravity == false){
	
		try{
		
			bool despawn = Me.GetValue<bool>("CorruptionDroneDespawn");
		
		}catch(Exception exc){
		
			//ModAPI Despawn Failed
		
		}
	
	}

}

void TryChat(string message){
	
	if(message == lastMessageSent){
		
		return;
		
	}
	
	try{
		
		Me.CustomData = message;
		bool despawn = Me.GetValue<bool>("CorruptionDroneChat");
		lastMessageSent = message;
	
	}catch(Exception exc){
	
		//ModAPI Chat Failed
	
	}

}

void TryIceRefill(){

	try{
		
		foreach(var block in blockList){
			
			var gasGenerator = block as IMyGasGenerator;
			
			if(gasGenerator != null){
				
				var inv = gasGenerator.GetInventory(0);
				
				if(inv.CurrentVolume == 0){
					
					bool refill = Me.GetValue<bool>("CorruptionIceRefill");
					
				}
				
			}
			
		}
		
	}catch(Exception exc){
	
		//ModAPI Refill Failed
	
	}

}

Vector3D randomRealignPosition(){
	
	MatrixD rcMatrix = MatrixD.CreateWorld(remoteControl.GetPosition(), remoteControl.WorldMatrix.Forward, remoteControl.WorldMatrix.Up);
	Vector3D coords = new Vector3D(0,0,0);
	coords.X = (double)rnd.Next(-500, 500);
	coords.Y = 600;
	coords.Z = (double)rnd.Next(-500, 500);
	var finalCoords = Vector3D.Transform(coords, rcMatrix);
	return finalCoords;
	
}

void ScriptSpeedRegular(){
	
	Runtime.UpdateFrequency |= UpdateFrequency.Update10;
	Runtime.UpdateFrequency &= ~UpdateFrequency.Update1;
	tickIncrement = 10;
	tickCounter = 0;
	
}

void ScriptSpeedFast(){
	
	Runtime.UpdateFrequency |= UpdateFrequency.Update1;
	Runtime.UpdateFrequency &= ~UpdateFrequency.Update10;
	tickIncrement = 1;
	tickCounter = 0;
	
}

void AutoRotateActivation(bool rotateMode){
	
	if(rotateMode == false){
		
		autoRotate = false;
		ScriptSpeedRegular();
		foreach(var gyro in gyroList){
			
			if(gyro != null){
				
				gyro.GyroOverride = false;
				
			}
			
		}
		
	}else{
		
		autoRotate = true;
		ScriptSpeedFast();
		
	}
	
}

void TryRotate(){
		
	if(autoRotate == true){
		
		if(remoteControl == null){
		
			return;
		
		}
		
		double yaw = 0;
		double pitch = 0;
		var targetDir = Vector3D.Normalize(remoteControl.GetPosition() - currentTarget);
		GetRotationAngles(targetDir * -1, (IMyTerminalBlock)remoteControl, out yaw, out pitch);
		ApplyGyroOverride(pitch, yaw, 0, gyroList, (IMyTerminalBlock)remoteControl);
		
	}	
	
	
	
}

Vector3D CreateDirectionAndTarget(Vector3D startDirCoords, Vector3D endDirCoords, Vector3D startPathCoords, double pathDistance){
	
	var direction = Vector3D.Normalize(endDirCoords - startDirCoords);
	var coords = direction * pathDistance + startPathCoords;
	return coords;
	
}

bool HasRangedTarget(MyDetectedEntityInfo scannedData, bool targetIsGrid, bool targetIsHostile, double maximumRange){
	
	if(scannedData.IsEmpty() == true){
		
		return false;
		
	}
	
	if(scannedData.HitPosition == null){
		
		return false;
		
	}
	
	if(MeasureDistance(dronePosition, (Vector3D)scannedData.HitPosition) > maximumRange){
		
		return false;
		
	}
	
	if(targetIsGrid == true && scannedData.Type.ToString().Contains("Grid") == false){
		
		return false;
		
	}
	
	if(scannedData.Relationship != MyRelationsBetweenPlayerAndBlock.Enemies && targetIsHostile == true){
		
		return false;
		
	}
	
	return true;
	
}

bool ApproximateTarget(Vector3D targetCoords, Vector3D myDirection, double maximumDistFromTarget){
	
	double distanceToTarget = MeasureDistance(targetCoords, dronePosition);
	var impactPosition = distanceToTarget * myDirection + dronePosition;
	
	if(MeasureDistance(impactPosition, targetCoords) < maximumDistFromTarget){
		
		return true;
		
	}
	
	return false;
	
}

void LaserFireAndDamage(){
	
	if(laserDamageTimer < 5){
		
		return;
		
	}
	
	laserDamageTimer = 0;
	
	if(laserCamera == null){
		
		return;
		
	}
	
	if(laserCamera.IsFunctional == false){
		
		return;
		
	}
	
	if(laserCamera.CanScan(2000) == false){
		
		return;
		
	}
	
	var scannedData = laserCamera.Raycast(2000, 0, 0);
	bool failedScan = false;
	
	if(scannedData.IsEmpty() == true){
		
		failedScan = true;
		
	}
	
	if(scannedData.HitPosition == null){
		
		failedScan = true;
		
	}
	
	Vector3D startCoords = new Vector3D(0,0,0);
	startCoords = laserCamera.WorldMatrix.Forward * 1 + laserCamera.GetPosition();
	
	if(failedScan == false){
		
		Vector3D endCoords = (Vector3D)scannedData.HitPosition;
		Me.CustomData = startCoords.ToString() + "\n";
		Me.CustomData += endCoords.ToString() + "\n";
		Me.CustomData += scannedData.EntityId.ToString();
		bool laserDamage = Me.GetValue<bool>("CorruptionLaserDamage");
		
	}
		
}

/*
/// Whip's Get Rotation Angles Method v12 - 2/16/18 ///
Dependencies: VectorAngleBetween()
* Fix to solve for zero cases when a vertical target vector is input
* Fixed straight up case
* Fixed sign on straight up case
* Converted math to local space
*/
void GetRotationAngles(Vector3D targetVector, IMyTerminalBlock reference, out double yaw, out double pitch)
{
    var localTargetVector = Vector3D.TransformNormal(targetVector, MatrixD.Transpose(reference.WorldMatrix));
    var flattenedTargetVector = new Vector3D(localTargetVector.X, 0, localTargetVector.Z);
    
    yaw = VectorAngleBetween(Vector3D.Forward, flattenedTargetVector) * Math.Sign(localTargetVector.X); //right is positive
    if (Math.Abs(yaw) < 1E-6 && localTargetVector.Z > 0) //check for straight back case
        yaw = Math.PI;
    
    if (Vector3D.IsZero(flattenedTargetVector)) //check for straight up case
        pitch = MathHelper.PiOver2 * Math.Sign(localTargetVector.Y);
    else
        pitch = VectorAngleBetween(localTargetVector, flattenedTargetVector) * Math.Sign(localTargetVector.Y); //up is positive
}

double VectorAngleBetween(Vector3D a, Vector3D b) //returns radians 
{
    if (Vector3D.IsZero(a) || Vector3D.IsZero(b))
        return 0;
    else
        return Math.Acos(MathHelper.Clamp(a.Dot(b) / Math.Sqrt(a.LengthSquared() * b.LengthSquared()), -1, 1));
}

//Whip's ApplyGyroOverride Method v9 - 8/19/17
void ApplyGyroOverride(double pitch_speed, double yaw_speed, double roll_speed, List<IMyGyro> gyro_list, IMyTerminalBlock reference) 
{ 
    var rotationVec = new Vector3D(-pitch_speed, yaw_speed, roll_speed); //because keen does some weird stuff with signs 
    var shipMatrix = reference.WorldMatrix;
    var relativeRotationVec = Vector3D.TransformNormal(rotationVec, shipMatrix); 

    foreach (var thisGyro in gyro_list) 
    { 
	
		if(thisGyro == null){
			
			continue;
			
		}
		
        var gyroMatrix = thisGyro.WorldMatrix;
        var transformedRotationVec = Vector3D.TransformNormal(relativeRotationVec, Matrix.Transpose(gyroMatrix)); 
 
        thisGyro.Pitch = (float)transformedRotationVec.X;
        thisGyro.Yaw = (float)transformedRotationVec.Y; 
        thisGyro.Roll = (float)transformedRotationVec.Z; 
        thisGyro.GyroOverride = true; 
    } 
}