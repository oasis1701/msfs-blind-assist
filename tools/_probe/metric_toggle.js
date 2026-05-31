(function(){ try {
  var k='A380X_CONFIG_USING_METRIC_UNIT';
  var before=(typeof GetStoredData!=='undefined')?GetStoredData(k,'0'):'NA';
  var nv=((''+before)==='1')?'0':'1';
  SetStoredData(k,nv);
  var after=GetStoredData(k,'0');
  return 'before='+before+' wrote='+nv+' after='+after;
} catch(e){ return 'ERR '+e; } })()