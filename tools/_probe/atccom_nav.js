(function(){ try {
  var mfd=document.querySelector('a380x-mfd'); if(!mfd||!mfd.fsInstrument) return 'no mfd';
  var fi=mfd.fsInstrument;
  function findUi(){
    if (fi.uiService && fi.uiService.navigateTo) return ['fi.uiService', fi.uiService];
    var refs=['mfdCaptRef','mfdFoRef'];
    for (var i=0;i<refs.length;i++){
      var r=fi[refs[i]];
      if (r && r.instance && r.instance.uiService && r.instance.uiService.navigateTo) return [refs[i]+'.instance.uiService', r.instance.uiService];
    }
    return [null,null];
  }
  var res=findUi(); var where=res[0], u=res[1];
  if (!u) return 'no uiService anywhere';
  u.navigateTo('atccom/msg-record');
  var au=(u.activeUri&&u.activeUri.get)?u.activeUri.get():null;
  return 'via '+where+'; navigated; active='+(au?JSON.stringify(au):'?');
} catch(e){ return 'ERR '+e+' '+(e&&e.stack); } })()