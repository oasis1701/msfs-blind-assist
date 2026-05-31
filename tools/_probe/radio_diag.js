(function(){ try {
  var A=window.__MSFSBA_A380; A.navigateUri('surv/controls');
  // Find all mfd-radio-button labels and report their input disabled/checked
  var labels=document.querySelectorAll('.mfd-radio-button');
  var out=[];
  for (var i=0;i<labels.length && i<24;i++){
    var lab=labels[i];
    var span=lab.querySelector('span');
    var inp=lab.querySelector('input[type=radio]');
    var txt=span?span.textContent:'?';
    var dis=inp?(inp.disabled?'DIS':'en'):'noinput';
    var chk=inp?(inp.checked?'CHK':'-'):'';
    out.push(txt+': '+dis+' '+chk);
  }
  return out.join('\n');
} catch(e){ return 'ERR '+e+' '+(e&&e.stack); } })()