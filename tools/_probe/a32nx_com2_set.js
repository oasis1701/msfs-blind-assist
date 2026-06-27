(function () {
  try {
    SimVar.SetSimVarValue('K:COM2_STBY_RADIO_SET_HZ', 'Hz', 121500000);
    return 'sent COM2 standby 121.500';
  } catch (e) { return 'ERR: ' + e; }
})()
