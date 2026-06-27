(function () {
  try {
    SimVar.SetSimVarValue('K:COM2_RADIO_SWAP', 'number', 0);
    return 'sent COM2 swap';
  } catch (e) { return 'ERR: ' + e; }
})()
