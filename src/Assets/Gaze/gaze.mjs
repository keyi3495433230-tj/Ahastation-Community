import {extractEyes,fitCalibration,predictGaze,GazeGate} from './gaze-math.mjs';
import {previewCrops,smoothCrop,previewPosition} from './gaze-preview.mjs';
const preview=document.querySelector('#preview'),eyeCanvas=document.querySelector('#eyePreview'),eyeCtx=eyeCanvas.getContext('2d');
let faceCrop=null,eyeCrop=null;
const video=document.querySelector('#video'),canvas=document.querySelector('#overlay'),ctx=canvas.getContext('2d'),status=document.querySelector('#status');
const layer=document.querySelector('#calibration'),target=document.querySelector('#target'),progress=document.querySelector('#progress'),hint=document.querySelector('#calibrationHint');
const gate=new GazeGate();
const profileKey='ahastation-gaze-calibration-v1';
let model,modelPromise,stream,running=false,epoch=0,generation=0,timer,calibration=null,profile=readProfile(),lastTime=-1;
const targets=[[.5,.5],[.08,.08],[.5,.08],[.92,.08],[.92,.5],[.92,.92],[.5,.92],[.08,.92],[.08,.5]];
function readProfile(){try{const value=localStorage.getItem(profileKey);if(!value)return null;const p=JSON.parse(value);return Array.isArray(p.means)&&p.means.length===4&&Array.isArray(p.scales)&&p.scales.length===4&&Array.isArray(p.coefficients)&&p.coefficients.length===2?p:null;}catch{return null;}}
function send(type,extra={}){window.chrome?.webview?.postMessage({type,epoch,...extra});}
function message(text){status.textContent=text;}
async function loadModel(){
  if(model)return model;
  modelPromise??=(async()=>{
    const {FaceLandmarker,FilesetResolver}=await import('./vendor/vision_bundle.mjs');
    const files=await FilesetResolver.forVisionTasks('./vendor/wasm');
    model=await FaceLandmarker.createFromOptions(files,{baseOptions:{modelAssetPath:'./face_landmarker.task',delegate:'CPU'},runningMode:'VIDEO',numFaces:2,minFaceDetectionConfidence:.65,minFacePresenceConfidence:.65,minTrackingConfidence:.65,outputFaceBlendshapes:true,outputFacialTransformationMatrixes:true});
    return model;
  })().catch(e=>{modelPromise=null;throw e;});
  return modelPromise;
}
async function start(session){
  stop(false);epoch=session;const ticket=generation;message('正在加载本地眼部识别模型…');
  try{
    await loadModel();if(ticket!==generation)return;
    const acquired=await navigator.mediaDevices.getUserMedia({audio:false,video:{facingMode:'user',width:{ideal:640},height:{ideal:480},frameRate:{ideal:15,max:20}}});
    if(ticket!==generation){acquired.getTracks().forEach(t=>t.stop());return;}
    stream=acquired;video.srcObject=stream;await video.play();
    if(ticket!==generation)return;
    stream.getVideoTracks()[0].addEventListener('ended',()=>{if(running)fail('摄像头已断开，请重新开启');},{once:true});
    running=true;document.body.classList.add('monitoring');lastTime=-1;gate.reset();send('started',{needsCalibration:!profile});message('摄像头已开启 · 头部与双眼自动放大，仅在本机识别');loop();
  }catch(e){if(ticket===generation)fail(e.name==='NotAllowedError'?'摄像头权限未开启，请在 Windows 隐私设置中允许摄像头访问':e.message||'无法启动摄像头');}
}
function stop(clearProfile=false){
  generation++;running=false;document.body.classList.remove('monitoring','looking-at-screen');clearTimeout(timer);calibration=null;hideCalibration();
  stream?.getTracks().forEach(t=>t.stop());stream=null;video.srcObject=null;gate.reset();if(clearProfile)profile=null;
  ctx.clearRect(0,0,canvas.width,canvas.height);eyeCtx.clearRect(0,0,eyeCanvas.width,eyeCanvas.height);faceCrop=eyeCrop=null;message('摄像头已关闭');
}
function fail(reason){stop(false);send('error',{reason});message(reason);}
function hideCalibration(){layer.hidden=true;document.body.classList.remove('calibrating');preview.style.removeProperty('left');preview.style.removeProperty('top');preview.style.removeProperty('visibility');}
function cancelCalibration(reason='校准已取消，请重新校准后开始计时'){
  if(!calibration)return;profile=calibration.previousProfile;calibration=null;gate.reset();hideCalibration();send('calibrationFailed',{reason,hasProfile:!!profile});message(reason);
}
function beginCalibration(geometry){
  if(!running)return;
  document.body.classList.remove('looking-at-screen');
  const previousProfile=profile;profile=null;gate.reset();calibration={geometry,index:0,groups:[],samples:[],started:performance.now(),width:innerWidth,height:innerHeight,previousProfile};
  layer.hidden=false;document.body.classList.add('calibrating');message('跟踪预览 · 头部与双眼');showTarget();
}
function showTarget(){
  const [x,y]=targets[calibration.index];target.style.left=(x*100)+'%';target.style.top=(y*100)+'%';
  progress.textContent='圆点 '+(calibration.index+1)+' / 9';hint.textContent='请注视圆点，等待自动切换';calibration.started=performance.now();calibration.samples=[];
  // Keep instructions away from the central fixation target.
  document.querySelector('header').style.top=calibration.index===0?'12%':'32%';
  const obstacles=[document.querySelector('header').getBoundingClientRect(),hint.getBoundingClientRect()];
  const position=previewPosition(innerWidth,innerHeight,preview.offsetWidth,preview.offsetHeight,x*innerWidth,y*innerHeight,obstacles);
  preview.style.visibility=position?'visible':'hidden';
  if(position){preview.style.left=position.left+'px';preview.style.top=position.top+'px';}
}
function collect(sample,now){
  const c=calibration;if(!c)return;
  if(Math.abs(innerWidth-c.width)>6||Math.abs(innerHeight-c.height)>6){cancelCalibration('窗口大小发生变化，请重新校准');return;}
  if(now-c.started>18000){cancelCalibration('没有持续看清双眼，请改善光线或调整坐姿后重新校准');return;}
  if(!sample.valid){hint.textContent=sample.reason;return;}
  if(now-c.started<800)return;
  c.samples.push(sample);hint.textContent='正在采集 '+Math.min(12,c.samples.length)+' / 12';
  if(c.samples.length<12)return;
  const [x,y]=targets[c.index],g=c.geometry;
  c.groups.push({target:[(g.left+x*g.width)/g.screenWidth,(g.top+y*g.height)/g.screenHeight],samples:c.samples});
  if(++c.index<targets.length){showTarget();return;}
  try{profile=fitCalibration(c.groups);localStorage.setItem(profileKey,JSON.stringify(profile));calibration=null;hideCalibration();gate.reset();send('calibrated');message('校准完成，已保存在本机；下次开启无需重新校准');}
  catch(e){cancelCalibration(e.message);}
}
function draw(result){
  const width=video.videoWidth,height=video.videoHeight;if(!width||!height)return;
  if(canvas.width!==400||canvas.height!==300){canvas.width=400;canvas.height=300;eyeCanvas.width=400;eyeCanvas.height=100;}
  const points=result.faceLandmarks?.length===1?result.faceLandmarks[0]:null;
  const crops=previewCrops(points,width,height);
  if(!crops){faceCrop=eyeCrop=null;ctx.clearRect(0,0,400,300);eyeCtx.clearRect(0,0,400,100);
    // Fit the entire raw frame without distortion when no single face is tracked.
    const scale=Math.min(400/width,300/height);ctx.drawImage(video,(400-width*scale)/2,(300-height*scale)/2,width*scale,height*scale);return;}
  faceCrop=smoothCrop(faceCrop,crops.face);eyeCrop=smoothCrop(eyeCrop,crops.eyes);
  drawCrop(ctx,canvas,faceCrop,points);drawCrop(eyeCtx,eyeCanvas,eyeCrop,points);
}
function drawCrop(context,surface,crop,points){
  context.drawImage(video,crop.x,crop.y,crop.width,crop.height,0,0,surface.width,surface.height);
  const point=id=>[(points[id].x*video.videoWidth-crop.x)/crop.width*surface.width,(points[id].y*video.videoHeight-crop.y)/crop.height*surface.height];
  context.strokeStyle='#55deab';context.lineWidth=1.5;
  for(const ids of [[33,160,158,133,153,144,33],[362,385,387,263,373,380,362]]){
    context.beginPath();ids.forEach((id,i)=>{const p=point(id);i?context.lineTo(...p):context.moveTo(...p);});context.stroke();
  }
  context.fillStyle='#ffc857';for(const id of [468,473]){context.beginPath();context.arc(...point(id),3,0,Math.PI*2);context.fill();}
}
function loop(){
  if(!running)return;
  const started=performance.now();
  try{
    if(video.readyState>=2&&video.currentTime!==lastTime){
      lastTime=video.currentTime;const result=model.detectForVideo(video,started);draw(result);
      const sample=extractEyes(result,video.videoWidth,video.videoHeight);
      if(calibration){message(sample.valid?'跟踪预览 · 头部与双眼':sample.reason);collect(sample,started);send('state',{state:'calibrating',reason:'正在校准视线'});}
      else {const value=gate.update(predictGaze(profile,sample),started);document.body.classList.toggle('looking-at-screen',value.state==='screen');send('state',value);message(value.state==='screen'?'视线位于屏幕 · 专注暂停':value.state==='away'?'视线已移开 · 专注计时':value.reason||'正在确认视线');}
    }
  }catch(e){fail('眼部识别出现错误：'+e.message);return;}
  timer=setTimeout(loop,Math.max(20,110-(performance.now()-started)));
}
function theme(dark,accent){document.documentElement.style.colorScheme=dark?'dark':'light';document.documentElement.style.setProperty('--bg',dark?'#2b2b2b':'#fbfbfb');document.documentElement.style.setProperty('--fg',dark?'#fafafa':'#202020');document.documentElement.style.setProperty('--accent',accent);}
window.gaze={start,stop,beginCalibration,cancelCalibration,theme,loadModel};
window.chrome?.webview?.addEventListener('message',e=>{const c=e.data;if(c.command==='start')start(c.epoch);else if(c.command==='calibrate')beginCalibration(c.geometry);else if(c.command==='theme')theme(c.dark,c.accent);});
document.querySelector('#cancel').addEventListener('click',()=>cancelCalibration());
document.addEventListener('keydown',e=>{if(e.key==='Escape')cancelCalibration();});
window.addEventListener('pagehide',()=>stop());
send('ready');
