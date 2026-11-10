# Local gaze runtime

- MediaPipe Tasks Vision: @mediapipe/tasks-vision **1.0.1**, Apache-2.0.
- Package: https://registry.npmjs.org/@mediapipe/tasks-vision/-/tasks-vision-1.0.1.tgz
- Package SHA-512: rvRE2FmAZ6ZxKSw7wq+e+jQDpN3t1B/tD2mJz9SmAzb1msoDkd4dMoE4wAh8Z30Um0PQwLiHr9QtomhmXk3aUQ==
- Face Landmarker float16 model, version 1: https://storage.googleapis.com/mediapipe-models/face_landmarker/face_landmarker/float16/1/face_landmarker.task
- Model SHA-256: 64184e229b263107bc2b804c6625db1341ff2bb731874b0bcc2fe6544e0bc9ff
- Upstream: https://github.com/google-ai-edge/mediapipe
- License: vendor/LICENSE. The distributed runtime also contains its upstream license notices.

All model and runtime assets are loaded from the local application directory. Camera frames and calibration measurements remain in memory. Gaze coordinates are a calibrated estimate from iris position and head pose, not a direct output or accuracy guarantee of MediaPipe.
