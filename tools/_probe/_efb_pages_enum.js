(function(){
  var out = { enum: {} };
  try{ var E = window.EfbPages; for(var k in E){ out.enum[k] = E[k]; } }catch(e){ out.err=e.message; }
  return JSON.stringify(out);
})()
