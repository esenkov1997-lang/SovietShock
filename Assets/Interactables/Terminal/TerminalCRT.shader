// Экран терминала "под ЭЛТ": изгиб кинескопа, скан-линии, RGB-маска, хроматическая аберрация,
// виньетка, мерцание, шум и бегущая полоса. Unlit — экран светится сам; яркость > 1 подхватит Bloom
// камеры игрока, если он включён в Global Volume.
//
// Картинку UI подставляет TerminalInstance в _BaseMap (через MaterialPropertyBlock), поэтому в самом
// материале текстура не нужна. Скан-линии и маска сами гаснут, когда экран далеко и полос больше, чем
// пикселей на экране, — иначе вместо полос был бы муар.
Shader "SovietShock/TerminalCRT"
{
	Properties
	{
		[MainTexture] _BaseMap ("Screen (подставляется TerminalInstance)", 2D) = "black" {}
		[HDR] _Tint ("Tint", Color) = (1, 1, 1, 1)
		_Brightness ("Brightness", Range(0, 5)) = 1.3

		[Header(Curvature)]
		_Curvature ("Curvature", Range(0, 0.5)) = 0.12
		_EdgeSoftness ("Edge Softness", Range(0.001, 0.05)) = 0.008
		_BezelColor ("Bezel Color (за краем изгиба)", Color) = (0, 0, 0, 1)

		[Header(Scanlines)]
		_ScanlineCount ("Scanline Count", Float) = 384
		_ScanlineIntensity ("Scanline Intensity", Range(0, 1)) = 0.35
		_ScanlineSpeed ("Scanline Scroll Speed", Float) = 0.02

		[Header(RGB Mask)]
		_MaskIntensity ("Mask Intensity", Range(0, 1)) = 0.2
		_MaskScale ("Mask Scale (триад на пиксель UI)", Range(0.1, 2)) = 0.5

		[Header(Chromatic Aberration)]
		_ChromaticAberration ("Chromatic Aberration", Range(0, 0.01)) = 0.0015

		[Header(Vignette)]
		_Vignette ("Vignette", Range(0, 1)) = 0.25

		[Header(Flicker and Noise)]
		_Flicker ("Flicker", Range(0, 0.3)) = 0.03
		_FlickerSpeed ("Flicker Speed", Float) = 30
		_Noise ("Noise", Range(0, 0.3)) = 0.03

		[Header(Roll Bar)]
		_RollBarIntensity ("Roll Bar Intensity", Range(0, 0.5)) = 0.06
		_RollBarSpeed ("Roll Bar Speed", Float) = 0.12
		_RollBarWidth ("Roll Bar Width", Range(0.01, 0.5)) = 0.12
	}

	SubShader
	{
		Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

		HLSLINCLUDE
		#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

		CBUFFER_START(UnityPerMaterial)
			float4 _BaseMap_ST;
			float4 _BaseMap_TexelSize;
			half4 _Tint;
			half _Brightness;
			half _Curvature;
			half _EdgeSoftness;
			half4 _BezelColor;
			float _ScanlineCount;
			half _ScanlineIntensity;
			float _ScanlineSpeed;
			half _MaskIntensity;
			half _MaskScale;
			half _ChromaticAberration;
			half _Vignette;
			half _Flicker;
			float _FlickerSpeed;
			half _Noise;
			half _RollBarIntensity;
			float _RollBarSpeed;
			half _RollBarWidth;
		CBUFFER_END
		ENDHLSL

		Pass
		{
			Name "UniversalForward"
			Tags { "LightMode" = "UniversalForward" }

			HLSLPROGRAM
			#pragma vertex Vert
			#pragma fragment Frag
			#pragma multi_compile_fog

			TEXTURE2D(_BaseMap);
			// clamp, а не repeat: RenderTexture по умолчанию повторяется, и аберрация у края тянула бы
			// пиксели с противоположной стороны экрана
			SAMPLER(sampler_crt_linear_clamp);

			struct Attributes
			{
				float4 positionOS : POSITION;
				float2 uv : TEXCOORD0;
			};

			struct Varyings
			{
				float4 positionCS : SV_POSITION;
				float2 uv : TEXCOORD0;
				half fogFactor : TEXCOORD1;
			};

			Varyings Vert(Attributes input)
			{
				Varyings output;
				output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
				output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
				output.fogFactor = ComputeFogFactor(output.positionCS.z);
				return output;
			}

			float Hash(float2 p)
			{
				p = frac(p * float2(123.34, 456.21));
				p += dot(p, p + 45.32);
				return frac(p.x * p.y);
			}

			half3 SampleScreen(float2 uv)
			{
				return SAMPLE_TEXTURE2D(_BaseMap, sampler_crt_linear_clamp, uv).rgb;
			}

			half4 Frag(Varyings input) : SV_Target
			{
				float time = _Time.y;

				// --- изгиб кинескопа: чем дальше от центра, тем сильнее "выпуклость" ---
				float2 centered = input.uv * 2.0 - 1.0;
				centered += centered * (centered.yx * centered.yx) * _Curvature;
				float2 uv = centered * 0.5 + 0.5;

				// всё, что после изгиба вышло за 0..1, — рамка кинескопа; мягкий край без "лесенки"
				float2 edge2 = smoothstep(0.0, _EdgeSoftness, uv) * smoothstep(0.0, _EdgeSoftness, 1.0 - uv);
				half edge = edge2.x * edge2.y;

				// --- хроматическая аберрация: R и B расходятся от центра ---
				float2 caOffset = centered * _ChromaticAberration;
				half3 color;
				color.r = SampleScreen(uv + caOffset).r;
				color.g = SampleScreen(uv).g;
				color.b = SampleScreen(uv - caOffset).b;

				// --- скан-линии (гаснут, когда линий больше, чем пикселей на экране) ---
				float linesPerPixel = fwidth(uv.y) * _ScanlineCount;
				half scanFade = saturate(2.0 - 2.0 * linesPerPixel);
				half scan = sin((uv.y + time * _ScanlineSpeed) * _ScanlineCount * PI) * 0.5 + 0.5;
				color *= 1.0 - _ScanlineIntensity * scanFade * (1.0 - scan);

				// --- RGB-маска (триады люминофора) ---
				float maskX = uv.x * _BaseMap_TexelSize.z * _MaskScale;
				half maskFade = saturate(2.0 - 2.0 * fwidth(maskX) * 3.0);
				float phase = frac(maskX) * 3.0;
				half dim = 1.0 - _MaskIntensity * maskFade;
				half3 mask = phase < 1.0 ? half3(1, dim, dim) : (phase < 2.0 ? half3(dim, 1, dim) : half3(dim, dim, 1));
				// маска затемняет — компенсируем, чтобы общая яркость не падала
				color *= mask / ((1.0 + 2.0 * dim) / 3.0);

				// --- бегущая полоса ---
				float barPos = frac(uv.y + time * _RollBarSpeed);
				half bar = smoothstep(_RollBarWidth, 0.0, abs(barPos - 0.5));
				color += (color + 0.02) * bar * _RollBarIntensity;

				// --- виньетка ---
				half vig = saturate(16.0 * uv.x * uv.y * (1.0 - uv.x) * (1.0 - uv.y));
				color *= pow(vig, _Vignette);

				// --- мерцание (скачет раз в 1/_FlickerSpeed сек) и шум ---
				color *= 1.0 - _Flicker * Hash(float2(floor(time * _FlickerSpeed), 7.0));
				color += (Hash(uv * _BaseMap_TexelSize.zw + frac(time) * 100.0) - 0.5) * _Noise;

				color = max(color, 0.0) * _Tint.rgb * _Brightness;
				color = lerp(_BezelColor.rgb, color, edge);
				color = MixFog(color, input.fogFactor);
				return half4(color, 1.0);
			}
			ENDHLSL
		}

		// нужен для depth prepass (SSAO, Depth Texture) — иначе экран "дырявый" в буфере глубины
		Pass
		{
			Name "DepthOnly"
			Tags { "LightMode" = "DepthOnly" }
			ZWrite On
			ColorMask R

			HLSLPROGRAM
			#pragma vertex DepthVert
			#pragma fragment DepthFrag

			float4 DepthVert(float4 positionOS : POSITION) : SV_POSITION
			{
				return TransformObjectToHClip(positionOS.xyz);
			}

			half DepthFrag() : SV_Target { return 0; }
			ENDHLSL
		}

		Pass
		{
			Name "DepthNormals"
			Tags { "LightMode" = "DepthNormals" }
			ZWrite On

			HLSLPROGRAM
			#pragma vertex DepthNormalsVert
			#pragma fragment DepthNormalsFrag

			struct DNAttributes
			{
				float4 positionOS : POSITION;
				float3 normalOS : NORMAL;
			};

			struct DNVaryings
			{
				float4 positionCS : SV_POSITION;
				float3 normalWS : TEXCOORD0;
			};

			DNVaryings DepthNormalsVert(DNAttributes input)
			{
				DNVaryings output;
				output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
				output.normalWS = TransformObjectToWorldNormal(input.normalOS);
				return output;
			}

			half4 DepthNormalsFrag(DNVaryings input) : SV_Target
			{
				return half4(NormalizeNormalPerPixel(input.normalWS), 0.0);
			}
			ENDHLSL
		}
	}

	FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
