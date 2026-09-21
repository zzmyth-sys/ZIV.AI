import os, sys, time, math, threading, traceback

COMFY = r"D:\devlop\ZIV.AI\Comfyui\ComfyUI"
DIT = r"C:\AI\ComfyUI_PIC\ComfyUI\models\diffusion_models\image2\qwen_image_2.1_int8_convrot.safetensors"
TE = r"C:\AI\ComfyUI_PIC\ComfyUI\models\text_encoders\qwen3.5_9b_qwen_image_2.1_pe_i2e.int8_convrot.safetensors".replace("i2e", "i2i")
VAE = r"C:\AI\ComfyUI_PIC\ComfyUI\models\vae\qwen_image_2.1_vae_bf16.safetensors"

sys.path.insert(0, COMFY)
os.chdir(COMFY)

import torch
import comfy.sd, comfy.utils, comfy.sample, comfy.samplers
import comfy.model_management as mm
import node_helpers
from comfy_extras.nodes_flux import get_schedule

RES = 512
STEPS = 20
INTERRUPT_AT = 5

def encode_qwen21(clip, vae, prompt, negative_prompt, resolution=1024):
    positive = clip.encode_from_tokens_scheduled(clip.tokenize(prompt, keep_vision=True, prevent_empty_text=True))
    negative = clip.encode_from_tokens_scheduled(clip.tokenize(negative_prompt, keep_vision=True, prevent_empty_text=True))
    latent = torch.zeros([1, 64, resolution // 16, resolution // 16], device=mm.intermediate_device())
    return positive, negative, {"samples": latent}

try:
    model = comfy.sd.load_diffusion_model(DIT)
    clip = comfy.sd.load_clip([TE], clip_type=comfy.sd.CLIPType.QWEN_IMAGE)
    vsd = comfy.utils.load_torch_file(VAE)
    vae = comfy.sd.VAE(vsd)

    positive, negative, latent = encode_qwen21(clip, vae, "A serene snowy mountain landscape at sunrise.", "", resolution=RES)
    seq_len = (RES * RES) // 256
    sigmas = torch.tensor(get_schedule(STEPS, seq_len), dtype=torch.float32)

    guider = comfy.samplers.CFGGuider(model)
    guider.set_conds(positive, negative)
    guider.set_cfg(1.0)
    sampler = comfy.samplers.sampler_object("euler")
    noise = comfy.sample.prepare_noise(latent["samples"], 42)

    calls = []
    fired = {"done": False}
    def cb(step, x0, x, total_steps):
        calls.append(step)
        print("CALLBACK step=%d/%d" % (step, total_steps))
        if step >= INTERRUPT_AT and not fired["done"]:
            fired["done"] = True
            def do_interrupt():
                time.sleep(0.2)
                print("INTERRUPT_CALL thread=%s" % threading.current_thread().name)
                mm.interrupt_current_processing(True)
            threading.Thread(target=do_interrupt, name="interrupter").start()
        return {}

    t = time.time()
    interrupted = False
    err = None
    try:
        samples = guider.sample(noise, latent["samples"], sampler, sigmas, disable_pbar=True, seed=42, callback=cb)
        print("sample_returned_normally")
    except Exception as e:
        interrupted = True
        err = repr(e)
        print("SAMPLE_RAISED %r" % e)
    dt = time.time() - t
    print("RESULT callbacks=%d last_step=%s elapsed_s=%.2f interrupted=%s err=%s"
          % (len(calls), (calls[-1] if calls else None), dt, interrupted, err))
    print("EXPECTED_INTERRUPT_AT=%d" % INTERRUPT_AT)
    print("CALLBACK_COUNT_OK=%s" % (len(calls) > 0))
except Exception as e:
    print("RESULT=FAIL err=%r" % e)
    traceback.print_exc()
