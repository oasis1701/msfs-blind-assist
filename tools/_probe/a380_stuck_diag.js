(function () {
  try {
    var r = {};
    r.parkBrake = SimVar.GetSimVarValue('BRAKE PARKING POSITION', 'Bool');
    r.brakeL = Math.round(SimVar.GetSimVarValue('BRAKE LEFT POSITION', 'percent'));
    r.brakeR = Math.round(SimVar.GetSimVarValue('BRAKE RIGHT POSITION', 'percent'));
    r.gs = SimVar.GetSimVarValue('GROUND VELOCITY', 'knots').toFixed(2);
    r.onGround = SimVar.GetSimVarValue('SIM ON GROUND', 'Bool');
    r.n1 = [1, 2, 3, 4].map(function (i) {
      return Math.round(SimVar.GetSimVarValue('TURB ENG N1:' + i, 'percent'));
    }).join('/');
    r.thrLever = [1, 2, 3, 4].map(function (i) {
      return Math.round(SimVar.GetSimVarValue('GENERAL ENG THROTTLE LEVER POSITION:' + i, 'percent'));
    }).join('/');
    r.engCombustion = [1, 2, 3, 4].map(function (i) {
      return SimVar.GetSimVarValue('GENERAL ENG COMBUSTION:' + i, 'Bool');
    }).join('/');
    r.tillerSteer = SimVar.GetSimVarValue('GEAR STEER ANGLE PCT:1', 'percent').toFixed(0);
    r.hdgTrue = (SimVar.GetSimVarValue('PLANE HEADING DEGREES TRUE', 'degrees')).toFixed(1);
    r.lat = SimVar.GetSimVarValue('PLANE LATITUDE', 'degrees').toFixed(7);
    r.lon = SimVar.GetSimVarValue('PLANE LONGITUDE', 'degrees').toFixed(7);
    r.chocks = SimVar.GetSimVarValue('L:FSDT_GSX_CHOCKS', 'number');
    r.gsxState = SimVar.GetSimVarValue('L:FSDT_GSX_DEBOARDING_STATE', 'number');
    r.gsxJetway = SimVar.GetSimVarValue('L:FSDT_GSX_JETWAY_STATE', 'number');
    r.crashed = SimVar.GetSimVarValue('CRASH FLAG', 'number');
    r.surfaceRel = SimVar.GetSimVarValue('SURFACE RELATIVE GROUND SPEED', 'knots').toFixed(2);
    return JSON.stringify(r);
  } catch (e) { return 'ERR ' + e; }
})()
