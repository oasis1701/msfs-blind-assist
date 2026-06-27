(function () {
  try {
    SimVar.SetSimVarValue('K:COM2_RADIO_SWAP', 'number', 0);
    SimVar.SetSimVarValue('K:COM2_STBY_RADIO_SET_HZ', 'Hz', 135400000);
    return 'restored COM2';
  } catch (e) { return 'ERR: ' + e; }
})()
