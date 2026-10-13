// Полноэкранные помехи "сбой сигнала" — для камеры шлема (смерть, урон). Родственник TerminalCRT:
// те же скан-линии, шум, бегущая полоса и мерцание, плюс то, что делает сбой узнаваемым: разрывы строк
// (полосы картинки сдвигаются вбок), прыжки кадра по вертикали, "битые" блоки и расхождение RGB-каналов.
//
// Подключается через URP Full Screen Pass Renderer Feature (материал с этим шейдером). Сила эффекта —
// глобальный параметр _SignalGlitchIntensity (0..1), его задаёт код (HUD.SignalGlitch).
//
// HUD: если на Canvas HUD висит HUDSignalRenderer, интерфейс рисуется в отдельную текстуру (_SignalHUDTexture),
// и этот шейдер накладывает его на кадр — с теми же искажениями, что и картинка (сила — HUD Glitch Amount).
// Поэтому эффект должен быть включён в рендерере ВСЕГДА: без него HUD не попадёт на экран.
Shader "SovietShock/SignalGlitch"
{
	Properties
	{
		[Header(HUD)]
		_HUDGlitchAmount ("HUD Glitch Amount (доля помех на HUD)", Range(0, 1)) = 1
		[Toggle] _HUDFlipY ("HUD Flip Y (если HUD перевёрнут)", Float) = 0

		[Header(Tearing)]
		_TearBands ("Tear Bands (полос по высоте)", Float) = 48
		_TearAmount ("Tear Amount (сдвиг полос)", Range(0, 0.2)) = 0.06
		_TearSpeed ("Glitch Rate (смен картинки в сек)", Float) = 24

		[Header(Frame Jump and Blocks)]
		_VerticalJump ("Vertical Jump", Range(0, 0.2)) = 0.04
		_BlockSize ("Block Size (блоков по высоте)", Float) = 18
		_BlockAmount ("Block Glitch Amount", Range(0, 1)) = 0.5

		[Header(Color)]
		_ChromaticAberration ("Chromatic Aberration", Range(0, 0.03)) = 0.012
		_Desaturate ("Desaturate", Range(0, 1)) = 0.4
		_Tint ("Tint (цвет обесцвеченной картинки)", Color) = (0.85, 1, 0.9, 1)

		[Header(CRT)]
		_ScanlineCount ("Scanline Count", Float) = 300
		_ScanlineIntensity ("Scanline Intensity", Range(0, 1)) = 0.35
		_RollBarIntensity ("Roll Bar Intensity", Range(0, 1)) = 0.3
		_RollBarSpeed ("Roll Bar Speed", Float) = 0.6
		_RollBarWidth ("Roll Bar Width", Range(0.01, 0.5)) = 0.08
		_Noise ("Noise (статика)", Range(0, 1)) = 0.25
		_Flicker ("Flicker", Range(0, 1)) = 0.35
	}

	SubShader
	{
		Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
		ZWrite Off ZTest Always Cull Off Blend Off

		Pass
		{
			Name "SignalGlitch"

			HLSLPROGRAM
			#pragma vertex Vert
			#pragma fragment Frag

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			// Vert, Varyings (texcoord) и _BlitTexture — картинка камеры, которую подаёт Full Screen Pass
			#include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

			// глобальные — задаёт код (HUD.SignalGlitch, HUD.HUDSignalRenderer), в материале не хранятся
			float _SignalGlitchIntensity;
			float _SignalHUDEnabled;
			TEXTURE2D(_SignalHUDTexture);

			float _HUDGlitchAmount;
			float _HUDFlipY;
			float _TearBands;
			float _TearAmount;
			float _TearSpeed;
			float _VerticalJump;
			float _BlockSize;
			float _BlockAmount;
			float _ChromaticAberration;
			float _Desaturate;
			float4 _Tint;
			float _ScanlineCount;
			float _ScanlineIntensity;
			float _RollBarIntensity;
			float _RollBarSpeed;
			float _RollBarWidth;
			float _Noise;
			float _Flicker;

			// clamp: сдвинутые полосы у края не должны тянуть пиксели с противоположной стороны экрана
			SAMPLER(sampler_glitch_linear_clamp);

			float Hash(float2 p)
			{
				p = frac(p * float2(123.34, 456.21));
				p += dot(p, p + 45.32);
				return frac(p.x * p.y);
			}

			half3 SampleScreen(float2 uv)
			{
				return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_glitch_linear_clamp, uv).rgb;
			}

			// HUD нарисован с премультиплицированной альфой (так рисует UI Unity): rgb уже умножен на прозрачность
			half4 SampleHUD(float2 uv)
			{
				if (_HUDFlipY > 0.5) uv.y = 1.0 - uv.y;
				return SAMPLE_TEXTURE2D(_SignalHUDTexture, sampler_glitch_linear_clamp, uv);
			}

			// RGB-расхождение: каналы читаются со сдвигом по горизонтали
			half3 SampleScreenSplit(float2 uv, float ca)
			{
				return half3(SampleScreen(uv + float2(ca, 0.0)).r, SampleScreen(uv).g, SampleScreen(uv - float2(ca, 0.0)).b);
			}

			half4 SampleHUDSplit(float2 uv, float ca)
			{
				half4 center = SampleHUD(uv);
				return half4(SampleHUD(uv + float2(ca, 0.0)).r, center.g, SampleHUD(uv - float2(ca, 0.0)).b, center.a);
			}

			// "Плёнка" сигнала поверх уже собранной картинки: обесцвечивание, скан-линии, полоса, статика, мерцание
			half3 ApplySignal(half3 color, float2 screenUV, float intensity, float time, float tick)
			{
				half luminance = dot(color, half3(0.299, 0.587, 0.114));
				color = lerp(color, luminance * _Tint.rgb, _Desaturate * intensity);

				half scan = sin(screenUV.y * _ScanlineCount * PI) * 0.5 + 0.5;
				color *= 1.0 - _ScanlineIntensity * intensity * (1.0 - scan);

				float barPos = frac(screenUV.y + time * _RollBarSpeed);
				half bar = smoothstep(_RollBarWidth, 0.0, abs(barPos - 0.5));
				color += bar * _RollBarIntensity * intensity * 0.5;

				// статика растёт быстрее остального (intensity²) — на максимуме почти "снег"
				half noise = Hash(screenUV * _ScreenParams.xy + frac(time) * 100.0);
				color = lerp(color, noise.xxx, _Noise * intensity * intensity);

				color *= 1.0 - _Flicker * intensity * Hash(float2(tick, 1.3));
				return color;
			}

			half4 Frag(Varyings input) : SV_Target
			{
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

				float2 screenUV = input.texcoord;
				bool hasHUD = _SignalHUDEnabled > 0.5;
				float intensity = saturate(_SignalGlitchIntensity);

				// без помех — картинка как есть, HUD просто поверх
				if (intensity <= 0.001)
				{
					half3 clean = SampleScreen(screenUV);
					if (hasHUD)
					{
						half4 hud = SampleHUD(screenUV);
						clean = clean * (1.0 - hud.a) + hud.rgb;
					}
					return half4(clean, 1.0);
				}

				float time = _Time.y;
				// помехи меняются скачками, а не плавно — как у настоящего сбоя
				float tick = floor(time * _TearSpeed);

				// --- общий сдвиг сигнала: прыжок кадра + разрыв полосы + битый блок ---
				float jumpOn = step(0.7, Hash(float2(tick, 9.7)));
				float jump = (Hash(float2(tick, 3.1)) - 0.5) * _VerticalJump * intensity * jumpOn;

				float band = floor(frac(screenUV.y + jump) * _TearBands);
				float bandOn = step(1.0 - intensity * 0.6, Hash(float2(band, tick)));
				float tear = (Hash(float2(band, tick + 17.0)) - 0.5) * 2.0 * _TearAmount * intensity * bandOn;

				float aspect = _ScreenParams.x / _ScreenParams.y;
				float2 jumped = float2(screenUV.x + tear, frac(screenUV.y + jump));
				float2 block = floor(float2(jumped.x * _BlockSize * aspect, jumped.y * _BlockSize));
				float blockOn = step(1.0 - _BlockAmount * intensity * 0.35, Hash(block + tick));
				float2 blockShift = blockOn * (float2(Hash(block + 5.0 + tick), Hash(block + 9.0 + tick)) - 0.5) * 0.08 * intensity;

				float2 offset = float2(tear, jump) + blockShift;
				float ca = _ChromaticAberration * intensity * (0.5 + 2.0 * abs(tear) / max(_TearAmount, 0.0001));

				// --- картинка ---
				float2 sceneUV = screenUV + offset;
				sceneUV.y = frac(sceneUV.y);
				half3 scene = SampleScreenSplit(sceneUV, ca);
				scene = lerp(scene, scene.brg, blockOn * 0.7);
				scene = ApplySignal(scene, screenUV, intensity, time, tick);

				if (!hasHUD) return half4(scene, 1.0);

				// --- HUD: те же искажения, но слабее/сильнее по HUD Glitch Amount ---
				float hudIntensity = intensity * _HUDGlitchAmount;
				float2 hudUV = screenUV + offset * _HUDGlitchAmount;
				hudUV.y = frac(hudUV.y);
				half4 hud = SampleHUDSplit(hudUV, ca * _HUDGlitchAmount);
				hud.rgb = lerp(hud.rgb, hud.brg, blockOn * 0.7 * _HUDGlitchAmount);
				// "плёнка" сигнала на HUD своей силы; работаем с не-премультиплицированным цветом, потом обратно
				half3 hudStraight = hud.a > 0.001 ? hud.rgb / hud.a : 0.0;
				hudStraight = ApplySignal(hudStraight, screenUV, hudIntensity, time, tick);

				return half4(scene * (1.0 - hud.a) + hudStraight * hud.a, 1.0);
			}
			ENDHLSL
		}
	}

	FallBack Off
}
