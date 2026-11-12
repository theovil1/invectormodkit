// The game's post-processing (its PostProcess profile), on an HDR target: a faint, wide bloom of what goes above
// the threshold, a vignette and a slight chromatic aberration.
import * as THREE from "three";
import { EffectComposer } from "../vendor/three/addons/postprocessing/EffectComposer.js";
import { RenderPass } from "../vendor/three/addons/postprocessing/RenderPass.js";
import { UnrealBloomPass } from "../vendor/three/addons/postprocessing/UnrealBloomPass.js";
import { ShaderPass } from "../vendor/three/addons/postprocessing/ShaderPass.js";
import { OutputPass } from "../vendor/three/addons/postprocessing/OutputPass.js";

export function createPost(renderer, scene, camera) {
  const composer = new EffectComposer(renderer);
  composer.addPass(new RenderPass(scene, camera));
  // PostProcessing v2 Bloom: threshold 2, intensity 0.1 (applied as 2^(0.1/10) - 1, about 0.007, over ~10 blurred
  // levels), diffusion 10, on HDR colours; three's bloom sums 5 mips, hence the low strength
  const bloom = new UnrealBloomPass(new THREE.Vector2(512, 512), 0.06, 0.6, 2);
  composer.addPass(bloom);
  composer.addPass(new ShaderPass({
    uniforms: { tDiffuse: { value: null }, vig: { value: 0.45 }, ca: { value: 0.15 } },
    vertexShader: "varying vec2 vUv; void main(){ vUv = uv; gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 1.0); }",
    fragmentShader: `uniform sampler2D tDiffuse; uniform float vig; uniform float ca; varying vec2 vUv;
      void main(){ vec2 d = vUv - 0.5; float r2 = dot(d, d);
        vec2 off = d * ca * 0.02 * (1.0 + r2 * 4.0);
        vec3 c = vec3(texture2D(tDiffuse, vUv + off).r, texture2D(tDiffuse, vUv).g, texture2D(tDiffuse, vUv - off).b);
        float v = smoothstep(0.85, 0.85 - 0.6 * vig, length(d * vec2(1.0, 0.75)) * 1.25);
        gl_FragColor = vec4(c * mix(1.0, v, vig * 1.4), 1.0); }`,
  }));
  composer.addPass(new OutputPass());
  return {
    resize(width, height) {
      composer.setPixelRatio(renderer.getPixelRatio());
      composer.setSize(width, height);
    },
    render() { composer.render(); },
    dispose() { composer.dispose(); },
  };
}
