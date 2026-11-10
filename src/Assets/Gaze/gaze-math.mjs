// Gaze estimation uses calibrated iris positions, not face presence.
const median = values => { const a = [...values].sort((x,y)=>x-y); return (a[Math.floor((a.length-1)/2)]+a[Math.ceil((a.length-1)/2)])/2; };
const dot = (a,b) => a.reduce((sum,x,i)=>sum+x*b[i],0);

export function extractEyes(result, width, height) {
  if (result.faceLandmarks?.length !== 1) return {valid:false, reason:result.faceLandmarks?.length>1?'画面中有多个人，请只保留一人':'未看清双眼，请调整光线或摄像头角度'};
  const p=result.faceLandmarks[0];
  if(p.length<478) return {valid:false,reason:'未识别到虹膜'};
  const pixel=i=>[p[i].x*width,p[i].y*height];
  function eye(a,b,upper,lower,iris) {
    const A=pixel(a),B=pixel(b),U=pixel(upper),L=pixel(lower),I=pixel(iris);
    const dx=B[0]-A[0],dy=B[1]-A[1],w=Math.hypot(dx,dy);
    if(w<12)return null;
    const h=((I[0]-A[0])*dx+(I[1]-A[1])*dy)/(w*w);
    const v=(-(I[0]-(A[0]+B[0])/2)*dy+(I[1]-(A[1]+B[1])/2)*dx)/(w*w);
    const openness=Math.abs(-(L[0]-U[0])*dy+(L[1]-U[1])*dx)/(w*w);
    return {h,v,openness};
  }
  const right=eye(33,133,159,145,468),left=eye(362,263,386,374,473);
  const blinks=result.faceBlendshapes?.[0]?.categories?.filter(c=>c.categoryName==='eyeBlinkLeft'||c.categoryName==='eyeBlinkRight')??[];
  if(!left||!right||Math.min(left.openness,right.openness)<.10||blinks.some(c=>c.score>.65))return {valid:false,reason:'眨眼或眼部被遮挡，暂无法判断视线'};
  if([left,right].some(e=>e.h<-.1||e.h>1.1||Math.abs(e.v)>.5)||Math.abs(left.h-right.h)>.35)return {valid:false,reason:'双眼跟踪不稳定，请调整光线'};
  const m=result.facialTransformationMatrixes?.[0]?.data;
  if(!m||m.length!==16)return {valid:false,reason:'未识别到头部姿态'};
  const yaw=Math.atan2(m[2],m[10]),pitch=Math.atan2(-m[6],Math.hypot(m[2],m[10]));
  const features=[(left.h+right.h)/2,(left.v+right.v)/2,yaw,pitch];
  if(!features.every(Number.isFinite))return {valid:false,reason:'眼部跟踪不稳定'};
  return {valid:true,features,center:[p[1].x,p[1].y],scale:Math.hypot(p[33].x-p[263].x,(p[33].y-p[263].y)*height/width)};
}

function solve(matrix, rhs) {
  const a=matrix.map((r,i)=>[...r,rhs[i]]), n=rhs.length;
  for(let j=0;j<n;j++) {
    let best=j;for(let i=j+1;i<n;i++)if(Math.abs(a[i][j])>Math.abs(a[best][j]))best=i;
    [a[j],a[best]]=[a[best],a[j]];
    if(Math.abs(a[j][j])<1e-9)throw Error('校准数据不足，请重新校准');
    const pivot=a[j][j];for(let k=j;k<=n;k++)a[j][k]/=pivot;
    for(let i=0;i<n;i++)if(i!==j){const f=a[i][j];for(let k=j;k<=n;k++)a[i][k]-=f*a[j][k];}
  }
  return a.map(r=>r[n]);
}

function regression(points) {
  const means=Array.from({length:4},(_,j)=>points.reduce((s,p)=>s+p.features[j],0)/points.length);
  const floors=[.025,.015,.08,.08];
  const scales=means.map((m,j)=>Math.max(floors[j],Math.sqrt(points.reduce((s,p)=>s+(p.features[j]-m)**2,0)/points.length)));
  const rows=points.map(p=>[1,...p.features.map((x,j)=>(x-means[j])/scales[j])]);
  const gram=Array.from({length:5},(_,j)=>Array.from({length:5},(_,k)=>rows.reduce((s,r)=>s+r[j]*r[k],0)+(j===k?(j===0?1e-8:.15):0)));
  const coefficients=[0,1].map(axis=>solve(gram,Array.from({length:5},(_,j)=>rows.reduce((s,r,i)=>s+r[j]*points[i].target[axis],0))));
  return {means,scales,coefficients};
}

function project(model, features) {const row=[1,...features.map((x,j)=>(x-model.means[j])/model.scales[j])];return model.coefficients.map(c=>dot(c,row));}

export function fitCalibration(groups) {
  if(groups.length!==9||groups.some(g=>g.samples.length<10))throw Error('校准未完成，请跟随全部九个圆点');
  const points=groups.map(g=>({target:g.target,features:Array.from({length:4},(_,j)=>median(g.samples.map(s=>s.features[j])))}));
  for(const [axis,minSpan] of [[0,.025],[1,.012]]) {
    const values=points.map(p=>p.features[axis]);
    if(Math.max(...values)-Math.min(...values)<minSpan)throw Error('没有捕捉到足够的眼球移动。请保持头部平稳，用眼睛跟随圆点重新校准');
  }
  const model=regression(points);
  const errors=points.map((p,i)=>{const testModel=regression(points.filter((_,j)=>i!==j));const predicted=project(testModel,p.features);return Math.hypot(predicted[0]-p.target[0],predicted[1]-p.target[1]);});
  const error=errors.reduce((a,b)=>a+b,0)/errors.length;
  if(!Number.isFinite(error)||error>.24||Math.max(...errors)>.5)throw Error('校准误差较大。请改善光线、保持头部平稳，再跟随圆点校准');
  const samples=groups.flatMap(g=>g.samples);
  return {...model,error,center:[0,1].map(j=>median(samples.map(s=>s.center[j]))),scale:median(samples.map(s=>s.scale)),pose:[2,3].map(j=>[Math.min(...points.map(p=>p.features[j]))-.4,Math.max(...points.map(p=>p.features[j]))+.4])};
}

export function predictGaze(model, sample) {
  if(!sample.valid)return {state:'unknown',reason:sample.reason};
  if(!model)return {state:'unknown',reason:'请先完成视线校准'};
  if(Math.hypot(sample.center[0]-model.center[0],sample.center[1]-model.center[1])>.18||sample.scale/model.scale<.65||sample.scale/model.scale>1.5||model.pose.some((range,j)=>sample.features[j+2]<range[0]||sample.features[j+2]>range[1]))return {state:'unknown',reason:'坐姿变化较大，请回到校准位置或重新校准'};
  const [x,y]=project(model,sample.features);
  if(!Number.isFinite(x)||!Number.isFinite(y))return {state:'unknown',reason:'视线跟踪不稳定'};
  const state=x>=-.06&&x<=1.06&&y>=-.06&&y<=1.06?'screen':x<-.14||x>1.14||y<-.14||y>1.14?'away':'boundary';
  return {state,x,y};
}

export class GazeGate {
  constructor(){this.reset();}
  reset(){this.state='unknown';this.awaySince=null;this.awayCount=0;}
  update(result, now) {
    if(result.state==='screen'){this.state='screen';this.awaySince=null;this.awayCount=0;}
    else if(result.state==='away'){
      this.awaySince??=now;this.awayCount++;
      if(this.awayCount>=3&&now-this.awaySince>=220)this.state='away';
    } else if(result.state==='unknown'){this.reset();}
    else {this.awaySince=null;this.awayCount=0;}
    return {...result,state:this.state};
  }
}
