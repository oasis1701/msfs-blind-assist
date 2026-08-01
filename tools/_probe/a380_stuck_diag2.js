(function () {
  try {
    var r = {};
    r.slew = SimVar.GetSimVarValue('IS SLEW ACTIVE', 'Bool');
    r.simDisabled = SimVar.GetSimVarValue('SIM DISABLED', 'Bool');
    r.pause = SimVar.GetSimVarValue('PAUSE FLAG', 'number');
    r.crashFlag = SimVar.GetSimVarValue('CRASH FLAG', 'number');
    r.crashSeq = SimVar.GetSimVarValue('CRASH SEQUENCE', 'number');
    r.velBodyZ = SimVar.GetSimVarValue('VELOCITY BODY Z', 'feet per second').toFixed(2);
    r.accelBodyZ = SimVar.GetSimVarValue('ACCELERATION BODY Z', 'feet per second squared').toFixed(3);
    r.n1 = [1, 2, 3, 4].map(function (i) { return Math.round(SimVar.GetSimVarValue('TURB ENG N1:' + i, 'percent')); }).join('/');
    r.thrust = [1, 2, 3, 4].map(function (i) { return Math.round(SimVar.GetSimVarValue('TURB ENG JET THRUST:' + i, 'pounds')); }).join('/');
    r.parkBrake = SimVar.GetSimVarValue('BRAKE PARKING POSITION', 'Bool');
    r.gearPos = SimVar.GetSimVarValue('GEAR POSITION:1', 'percent');
    r.gearDamage = SimVar.GetSimVarValue('GEAR DAMAGE BY SPEED', 'Bool');
    r.wheelRpm = SimVar.GetSimVarValue('WHEEL RPM:1', 'RPM').toFixed(1);
    r.surfaceType = SimVar.GetSimVarValue('SURFACE TYPE', 'Enum');
    r.altAgl = SimVar.GetSimVarValue('PLANE ALT ABOVE GROUND', 'feet').toFixed(2);
    r.pitch = SimVar.GetSimVarValue('PLANE PITCH DEGREES', 'degrees').toFixed(2);
    r.bank = SimVar.GetSimVarValue('PLANE BANK DEGREES', 'degrees').toFixed(2);
    r.totalWeight = Math.round(SimVar.GetSimVarValue('TOTAL WEIGHT', 'pounds'));
    r.contactPointCompression = [0,1,2].map(function (i) {
      return SimVar.GetSimVarValue('CONTACT POINT COMPRESSION:' + i, 'percent').toFixed(0);
    }).join('/');
    return JSON.stringify(r);
  } catch (e) { return 'ERR ' + e; }
})()
