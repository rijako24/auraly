import {expect,test} from "@playwright/test";

for (const scenario of ["denegado", "otra identidad", "mis datos"] as const) {
  test(`caché de despacho: ${scenario}`, async ({page,context,baseURL}) => {
    test.setTimeout(120_000);
    const tenantId="11111111-1111-1111-1111-111111111111",businessId="22222222-2222-2222-2222-222222222222",userId="33333333-3333-3333-3333-333333333333";
    const user={userId,tenantId,tenantKey:"@test",username:"driver",firstName:"Transportador",lastName:"Prueba",roles:[],permissions:["dispatches.read","dispatches.delivery.execute"]};
    await context.addCookies([{name:"auth_token",value:"e2e",url:baseURL!,httpOnly:true}]);
    await page.addInitScript(({user,businessId})=>{
      localStorage.setItem("selected_tenant_id",user.tenantId);localStorage.setItem("selected_business_id",businessId);
      localStorage.setItem("auth-state",JSON.stringify({state:{isAuthenticated:true,user},version:0}));
    },{user,businessId});
    const dispatch={dispatchId:"isolation",dispatchNumber:"DSP-AISLAMIENTO",scheduledDate:"2026-11-18",driverName:"Transportador",status:"InDelivery",documentCount:1,lineCount:1,expectedQuantity:1,verifiedQuantity:1,shortageQuantity:0,rowVersion:"AAAAAAAAAAA="};
    const execution={...dispatch,documents:[{dispatchSourceDocumentId:"source",sourceDocumentId:"sale",documentType:"SalesReceipt",documentNumber:"CVI-PRIVADO",customerName:"Cliente privado",documentTotal:50000,creditAmount:0,collectionMode:"Dispatch",sequence:1,deliveryStatus:"Pending",payments:[],returns:[],lines:[],notes:null}],expenses:[],settlement:null};
    let allow=true,executionReads=0;
    await page.route("**/api/**",async route=>{
      const path=new URL(route.request().url()).pathname;let body:unknown=[];
      if(path.endsWith("/auth/me"))body=user;
      else if(path.endsWith("/execution-context/tenants"))body=[{tenantId,name:"Empresa"}];
      else if(path.endsWith("/execution-context/businesses"))body=[{tenantId,businessId,name:"Sede",timeZone:"America/Bogota"}];
      else if(path.endsWith("/execution-context/access"))body={tenantId,businessId,roles:[],permissions:user.permissions};
      else if(path.endsWith("/execution")){
        executionReads++;if(!allow){await route.fulfill({status:403,contentType:"application/json",body:JSON.stringify({message:"Sin permiso para este despacho"})});return;}body=execution;
      }else if(path.endsWith("/dispatches"))body={items:[dispatch],page:1,pageSize:50,totalCount:1,totalPages:1};
      await route.fulfill({status:200,contentType:"application/json",body:JSON.stringify(body)});
    });
    await page.setViewportSize({width:390,height:844});await page.goto("/dashboard/deliveries");
    await page.getByRole("button",{name:/DSP-AISLAMIENTO/}).click();
    await expect(page.getByRole("button",{name:/Cliente privado CVI-PRIVADO/})).toBeVisible();
    if(scenario==="denegado"){
      allow=false;await page.getByRole("button",{name:"Actualizar despacho",exact:true}).click();
      await expect(page.getByText("No pudimos abrir este despacho",{exact:true})).toBeVisible();
      await expect(page.getByRole("button",{name:/Cliente privado CVI-PRIVADO/})).toHaveCount(0);
    }else{
      if(scenario==="otra identidad") await page.evaluate(async({tenantId,businessId,userId})=>{
        await new Promise<void>((resolve,reject)=>{
          const request=indexedDB.open("auraly-dispatch-pwa",5);
          request.onsuccess=()=>{const db=request.result,tx=db.transaction("dispatch-snapshots","readwrite"),store=tx.objectStore("dispatch-snapshots");
            const key=`${tenantId}:${businessId}:${userId}:isolation`,read=store.get(key);
            read.onsuccess=()=>{const value=read.result;store.delete(key);store.put({...value,scope:"foreign",dispatchId:"foreign:isolation"});};
            tx.oncomplete=()=>{db.close();resolve();};tx.onerror=()=>reject(tx.error);};request.onerror=()=>reject(request.error);
        });
      },{tenantId,businessId,userId});
      await context.setOffline(true);
      await page.getByRole("button",{name:"Actualizar despacho",exact:true}).click();
      if(scenario==="otra identidad"){
        await expect(page.getByText("No pudimos abrir este despacho",{exact:true})).toBeVisible();
        await expect(page.getByRole("button",{name:/Cliente privado CVI-PRIVADO/})).toHaveCount(0);
      }else{
        await expect(page.getByRole("button",{name:/Cliente privado CVI-PRIVADO/})).toBeVisible();
        expect(executionReads).toBe(1);
      }
    }
  });
}


test("la cola local envía solo un lote de su identidad y conserva los pendientes ajenos e históricos", async ({page,baseURL}) => {
  test.setTimeout(120_000);
  const tenantId="11111111-1111-1111-1111-111111111111",businessId="22222222-2222-2222-2222-222222222222",userId="33333333-3333-3333-3333-333333333333";
  const user={userId,tenantId,tenantKey:"@test",username:"driver",firstName:"Transportador",lastName:"Prueba",roles:[],permissions:["dispatches.read","dispatches.delivery.execute"]};
  await page.context().addCookies([{name:"auth_token",value:"e2e",url:baseURL!,httpOnly:true}]);
  await page.addInitScript(({user,businessId})=>{localStorage.setItem("selected_tenant_id",user.tenantId);localStorage.setItem("selected_business_id",businessId);localStorage.setItem("auth-state",JSON.stringify({state:{isAuthenticated:true,user},version:0}))},{user,businessId});
  const submitted:string[]=[];
  await page.route("**/api/**",async route=>{
    const path=new URL(route.request().url()).pathname;let body:unknown=[];
    if(path.endsWith("/auth/me"))body=user;
    else if(path.endsWith("/execution-context/tenants"))body=[{tenantId,name:"Empresa"}];
    else if(path.endsWith("/execution-context/businesses"))body=[{tenantId,businessId,name:"Sede",timeZone:"America/Bogota"}];
    else if(path.endsWith("/execution-context/access"))body={tenantId,businessId,roles:[],permissions:user.permissions};
    else if(path.endsWith("/dispatches"))body={items:[],page:1,pageSize:50,totalCount:0,totalPages:0};
    else if(path.endsWith("/delivery-results")){submitted.push(route.request().postDataJSON().idempotencyKey);body={dispatchId:"batch",dispatchNumber:"DSP-BATCH",scheduledDate:"2026-11-18",driverName:"Transportador",status:"InDelivery",documents:[],expenses:[],settlement:null};}
    await route.fulfill({status:200,contentType:"application/json",body:JSON.stringify(body)});
  });
  await page.goto("/dashboard/deliveries");
  await expect(page.getByText("No tienes despachos asignados",{exact:true})).toBeVisible();
  await expect(page.getByText("Sincronizando…",{exact:true})).toHaveCount(0);
  await page.evaluate(async({tenantId,businessId,userId})=>{
    await new Promise<void>((resolve,reject)=>{
      const request=indexedDB.open("auraly-dispatch-pwa",5);
      request.onsuccess=()=>{
        const db=request.result,tx=db.transaction("dispatch-outbox","readwrite"),store=tx.objectStore("dispatch-outbox");
        const scope=`${tenantId}:${businessId}:${userId}`;
        for(let index=0;index<32;index++){
          const key=index<30?`own-${index}`:index===30?"foreign":"legacy";
          const owner=index<30?scope:index===30?"foreign":undefined;
          store.put({id:key,scope:owner,dispatchId:"batch",kind:"delivery",evidence:[],attempts:0,queuedAt:`2026-11-18T12:00:${String(index).padStart(2,"0")}Z`,request:{dispatchSourceDocumentId:key,deliveryStatus:"Delivered",reason:null,notes:null,latitude:null,longitude:null,occurredAt:"2026-11-18T12:00:00Z",idempotencyKey:key,payments:[],returns:[]}});
        }
        tx.oncomplete=()=>{db.close();resolve();};tx.onerror=()=>reject(tx.error);
      };request.onerror=()=>reject(request.error);
    });
  },{tenantId,businessId,userId});
  await page.evaluate(()=>window.dispatchEvent(new Event("focus")));
  await expect.poll(()=>submitted.length).toBe(20);
  await expect(page.getByText("Sincronizando…",{exact:true})).toHaveCount(0);
  expect(submitted.every(value=>value.startsWith("own-"))).toBe(true);
  await expect(page.getByText("Hay entregas sin conexión de una versión anterior que requieren revisión antes de enviarse.",{exact:true})).toBeVisible();
  const remaining=await page.evaluate(async()=>await new Promise<number>((resolve,reject)=>{
    const request=indexedDB.open("auraly-dispatch-pwa",5);
    request.onsuccess=()=>{const db=request.result,tx=db.transaction("dispatch-outbox","readonly"),count=tx.objectStore("dispatch-outbox").count();count.onsuccess=()=>resolve(count.result);tx.oncomplete=()=>db.close();tx.onerror=()=>reject(tx.error);};request.onerror=()=>reject(request.error);
  }));
  expect(remaining).toBe(12);
  await expect(page.getByText("Quedan 10 cambios de despacho por enviar.",{exact:true})).toBeVisible();
});

for(const interruption of ["segunda subida","conexión de entrega","conexión de gasto","otra pestaña","navegador sin exclusión"] as const) {
test(`el reintento conserva los soportes y la operación ante fallo de ${interruption}`, async ({page,baseURL,context}) => {
  test.setTimeout(120_000);
  const tenantId="11111111-1111-1111-1111-111111111111",businessId="22222222-2222-2222-2222-222222222222",userId="33333333-3333-3333-3333-333333333333";
  const user={userId,tenantId,tenantKey:"@test",username:"driver",firstName:"Transportador",lastName:"Prueba",roles:[],permissions:["dispatches.read","dispatches.delivery.execute"]};
  await page.context().addCookies([{name:"auth_token",value:"e2e",url:baseURL!,httpOnly:true}]);
  await context.addInitScript(({user,businessId})=>{localStorage.setItem("selected_tenant_id",user.tenantId);localStorage.setItem("selected_business_id",businessId);localStorage.setItem("auth-state",JSON.stringify({state:{isAuthenticated:true,user},version:0}))},{user,businessId});
  if(interruption==="navegador sin exclusión")await context.addInitScript(()=>Object.defineProperty(navigator,"locks",{value:undefined}));
  const uploads:string[]=[];
  const submitted:Array<{payments?:Array<{evidenceUrl:string}>;evidenceUrl?:string;idempotencyKey:string}> = [];
  let failedSecond=false,offline=false;
  let releaseUpload:()=>void=()=>{};
  const firstUploadGate=new Promise<void>(resolve=>{releaseUpload=resolve;});
  await context.route("**/api/**",async route=>{
    const path=new URL(route.request().url()).pathname;let body:unknown=[];
    if(path.endsWith("/auth/me"))body=user;
    else if(path.endsWith("/execution-context/tenants"))body=[{tenantId,name:"Empresa"}];
    else if(path.endsWith("/execution-context/businesses"))body=[{tenantId,businessId,name:"Sede",timeZone:"America/Bogota"}];
    else if(path.endsWith("/execution-context/access"))body={tenantId,businessId,roles:[],permissions:user.permissions};
    else if(path.endsWith("/dispatches"))body={items:[],page:1,pageSize:50,totalCount:0,totalPages:0};
    else if(path.endsWith("/evidence")){
      const filename=/filename="([^"]+)"/.exec(route.request().postDataBuffer()?.toString("utf8")??"")?.[1];
      expect(filename).toMatch(/^(primero|segundo)\.png$/);
      uploads.push(filename!);
      if(interruption==="otra pestaña"&&uploads.length===1)await firstUploadGate;
      if(filename==="segundo.png"&&!failedSecond){failedSecond=true;await route.fulfill({status:503,contentType:"application/json",body:JSON.stringify({message:"Fallo al subir el segundo soporte."})});return;}
      if(interruption!=="segunda subida"&&interruption!=="otra pestaña"&&!offline){offline=true;await context.setOffline(true);}
      body={url:`https://qa.invalid/${filename}`};
    }else if(path.endsWith("/delivery-results")||path.endsWith("/expenses")){
      if(offline){await route.abort("internetdisconnected");return;}
      submitted.push(route.request().postDataJSON());
      body={dispatchId:"proof-batch",dispatchNumber:"DSP-SOPORTES",scheduledDate:"2026-11-18",driverName:"Transportador",status:"InDelivery",documents:[],expenses:[],settlement:null};
    }
    await route.fulfill({status:200,contentType:"application/json",body:JSON.stringify(body)});
  });
  await page.goto("/dashboard/deliveries");
  await expect(page.getByText("No tienes despachos asignados",{exact:true})).toBeVisible();
  await expect(page.getByText("Sincronizando…",{exact:true})).toHaveCount(0);
  if(interruption==="navegador sin exclusión")await expect(page.getByText("Este navegador no permite sincronizar los despachos de forma segura. Usa un navegador actualizado; los cambios se conservan.",{exact:true})).toHaveCount(0);
  await page.evaluate(async({tenantId,businessId,userId,interruption})=>{
    await new Promise<void>((resolve,reject)=>{
      const request=indexedDB.open("auraly-dispatch-pwa",5);
      request.onsuccess=()=>{
        const db=request.result,tx=db.transaction(["dispatch-outbox","dispatch-snapshots"],"readwrite"),scope=`${tenantId}:${businessId}:${userId}`;
        const evidence=(interruption==="segunda subida"?["primero","segundo"]:["primero"]).map(name=>({placeholder:`local:${name}`,file:new File([Uint8Array.of(1)],`${name}.png`,{type:"image/png"})}));
        tx.objectStore("dispatch-snapshots").put({dispatchId:`${scope}:proof-batch`,scope,dispatchNumber:"DSP-SOPORTES",status:"InDelivery",expenses:[],settlement:null,
          documents:[{dispatchSourceDocumentId:"source",sourceDocumentId:"sale",documentType:"SalesReceipt",documentNumber:"CVI-SOPORTES",customerName:"Cliente",documentTotal:50000,creditAmount:0,collectionMode:"Dispatch",deliveryStatus:"Pending",lines:[],payments:[],returns:[]}]});
        tx.objectStore("dispatch-outbox").put({id:`${scope}:two-proofs`,scope,dispatchId:"proof-batch",kind:interruption==="conexión de gasto"?"expense":"delivery",evidence,attempts:0,queuedAt:"2026-11-18T12:00:00Z",
          request:interruption==="conexión de gasto"?{category:"Gasto del recorrido",amount:50000,description:null,evidenceUrl:evidence[0].placeholder,idempotencyKey:"two-proofs",occurredAt:"2026-11-18T12:00:00Z"}:
            {dispatchSourceDocumentId:"source",deliveryStatus:"Delivered",reason:null,notes:null,latitude:null,longitude:null,occurredAt:"2026-11-18T12:00:00Z",idempotencyKey:"two-proofs",returns:[],
              payments:evidence.map((item,index)=>({applicationType:"InvoicePayment",amount:50000/evidence.length,paymentMethod:"Transfer",paymentMethodId:`transfer-${index}`,paymentMethodVersion:1,reference:`REF-${index}`,evidenceUrl:item.placeholder}))}});
        tx.oncomplete=()=>{db.close();resolve();};tx.onerror=()=>reject(tx.error);
      };request.onerror=()=>reject(request.error);
    });
  },{tenantId,businessId,userId,interruption});
  await page.evaluate(()=>window.dispatchEvent(new Event("focus")));
  if(interruption==="navegador sin exclusión"){
    await expect(page.getByText("Este navegador no permite sincronizar los despachos de forma segura. Usa un navegador actualizado; los cambios se conservan.",{exact:true})).toBeVisible();
    await expect(page.getByText("Quedan 1 cambios de despacho por enviar.",{exact:true})).toBeVisible();
    await expect(page.getByText("Sincronizando…",{exact:true})).toHaveCount(0);
    expect(uploads).toHaveLength(0);expect(submitted).toHaveLength(0);
    return;
  }
  if(interruption==="segunda subida") {
    await expect(page.getByText("Fallo al subir el segundo soporte.",{exact:true})).toBeVisible();
    expect(uploads).toEqual(["primero.png","segundo.png"]);
    expect(submitted).toHaveLength(0);
    await page.reload();
  }else if(interruption==="otra pestaña"){
    await expect.poll(()=>uploads.length).toBe(1);
    const otherPage=await context.newPage();
    try {
      await otherPage.goto("/dashboard/deliveries");
      await expect(otherPage.getByText("Quedan 1 cambios de despacho por enviar.",{exact:true})).toBeVisible();
      expect(uploads).toEqual(["primero.png"]);
      expect(submitted).toHaveLength(0);
    }finally{releaseUpload();await otherPage.close();}
  }else{
    await expect(page.getByText("Quedan 1 cambios de despacho por enviar.",{exact:true})).toBeVisible();
    expect(submitted).toHaveLength(0);expect(uploads).toEqual(["primero.png"]);
    offline=false;await context.setOffline(false);
  }
  await expect.poll(()=>submitted.length).toBe(1);
  await expect(page.getByText("Sincronizando…",{exact:true})).toHaveCount(0);
  expect(submitted).toHaveLength(1);
  expect(uploads).toEqual(interruption==="segunda subida"?["primero.png","segundo.png","segundo.png"]:["primero.png"]);
  expect(submitted[0].idempotencyKey).toBe("two-proofs");
  if(interruption==="conexión de gasto")expect(submitted[0].evidenceUrl).toBe("https://qa.invalid/primero.png");
  else expect(submitted[0].payments!.map(payment=>payment.evidenceUrl)).toEqual(interruption==="segunda subida"?["https://qa.invalid/primero.png","https://qa.invalid/segundo.png"]:["https://qa.invalid/primero.png"]);
});
}
