// Полноэкранные помехи "сбой сигнала" — для камеры шлема (смерть, позже — сильный урон). Родственник TerminalCRT:
// те же скан-линии, шум, бегущая полоса и мерцание, плюс то, что делает сбой узнаваемым: разрывы строк
// (полосы картинки сдвигаются вбок), прыжки кадра по вертикали, "битые" блоки и расхождение RGB-каналов.
//
// Подключается через URP Full Screen Pass Renderer Feature (материал с этим шейдером). Сила эффекта —
// глобальный параметр _SignalGlitchIntensity (0..1), его задаёт код (HUD.SignalGlitch). При 0 картинка
// проходит без изменений, поэтому эффект можно держать включённым в рендерере всегда.
Shader "SovietShock/SignalGlitch"
{
	Properties
	{
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

			// глобальная — задаёт HUD.SignalGlitch.SetIntensity, не хранится в материале
			float _SignalGlitchIntensity;

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

			half4 Frag(Varyings input) : SV_Target
			{
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

				float2 screenUV = input.texcoord;
				float intensity = saturate(_SignalGlitchIntensity);
				if (intensity <= 0.001) return half4(SampleScreen(screenUV), 1.0);

				float time = _Time.y;
				// помехи меняются скачками, а не плавно — как у настоящего сбоя
				float tick = floor(time * _TearSpeed);
				float2 uv = screenUV;

				// --- прыжок всего кадра по вертикали (иногда) ---
				float jumpOn = step(0.7, Hash(float2(tick, 9.7)));
				uv.y = frac(uv.y + (Hash(float2(tick, 3.1)) - 0.5) * _VerticalJump * intensity * jumpOn);

				// --- разрывы строк: часть полос сдвигается вбок; чем сильнее сбой, тем больше полос ---
				float band = floor(uv.y * _TearBands);
				float bandOn = step(1.0 - intensity * 0.6, Hash(float2(band, tick)));
				float tear = (Hash(float2(band, tick + 17.0)) - 0.5) * 2.0 * _TearAmount * intensity * bandOn;
				uv.x += tear;

				// --- "битые" блоки: прямоугольники картинки съезжают и меняют цвет ---
				float aspect = _ScreenParams.x / _ScreenParams.y;
				float2 block = floor(float2(uv.x * _BlockSize * aspect, uv.y * _BlockSize));
				float blockOn = step(1.0 - _BlockAmount * intensity * 0.35, Hash(block + tick));
				uv += blockOn * (float2(Hash(block + 5.0 + tick), Hash(block + 9.0 + tick)) - 0.5) * 0.08 * intensity;

				// --- расхождение RGB: сильнее на сдвинутых полосах ---
				float ca = _ChromaticAberration * intensity * (0.5 + 2.0 * abs(tear) / max(_TearAmount, 0.0001));
				half3 color;
				color.r = SampleScreen(uv + float2(ca, 0.0)).r;
				color.g = SampleScreen(uv).g;
				color.b = SampleScreen(uv - float2(ca, 0.0)).b;
				color = lerp(color, color.brg, blockOn * 0.7);

				// --- обесцвечивание в тон сигнала ---
				half luminance = dot(color, half3(0.299, 0.587, 0.114));
				color = lerp(color, luminance * _Tint.rgb, _Desaturate * intensity);

				// --- скан-линии и бегущая полоса (по экрану, без сдвигов — "стекло" монитора не рвётся) ---
				half scan = sin(screenUV.y * _ScanlineCount * PI) * 0.5 + 0.5;
				color *= 1.0 - _ScanlineIntensity * intensity * (1.0 - scan);
				float barPos = frac(screenUV.y + time * _RollBarSpeed);
				half bar = smoothstep(_RollBarWidth, 0.0, abs(barPos - 0.5));
				color += bar * _RollBarIntensity * intensity * 0.5;

				// --- статика: растёт быстрее остального (intensity²), на максимуме почти "снег" ---
				half noise = Hash(screenUV * _ScreenParams.xy + frac(time) * 100.0);
				color = lerp(color, noise.xxx, _Noise * intensity * intensity);

				// --- мерцание яркости скачками ---
				color *= 1.0 - _Flicker * intensity * Hash(float2(tick, 1.3));

				return half4(color, 1.0);
			}
			ENDHLSL
		}
	}

	FallBack Off
}
