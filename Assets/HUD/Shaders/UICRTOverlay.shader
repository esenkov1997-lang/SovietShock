// CRT-накладка для UI: полупрозрачный слой поверх интерфейса (экран смерти "сигнал потерян" и т.п.) —
// скан-линии, виньетка, бегущая полоса, статика ("снег") и мерцание. Тот же набор, что у TerminalCRT,
// но в виде UI-шейдера: ставится на Image, растянутый на весь экран, ПОСЛЕДНИМ в панели (поверх текста и кнопок).
// Сам Image может быть без спрайта; цвет Image умножает накладку (альфа Image — общая сила эффекта).
// Image с этим материалом должен быть с выключенным Raycast Target, иначе он закроет кнопки от кликов.
Shader "SovietShock/UICRTOverlay"
{
	Properties
	{
		[PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
		_Color ("Tint", Color) = (1, 1, 1, 1)

		[Header(Scanlines)]
		_ScanlineCount ("Scanline Count", Float) = 300
		_ScanlineIntensity ("Scanline Intensity (затемнение)", Range(0, 1)) = 0.4
		_ScanlineSpeed ("Scanline Scroll Speed", Float) = 0.02

		[Header(Vignette)]
		_Vignette ("Vignette", Range(0, 1)) = 0.5

		[Header(Roll Bar)]
		_RollBarIntensity ("Roll Bar Intensity", Range(0, 1)) = 0.08
		_RollBarSpeed ("Roll Bar Speed", Float) = 0.12
		_RollBarWidth ("Roll Bar Width", Range(0.01, 0.5)) = 0.12

		[Header(Static and Flicker)]
		_Noise ("Noise (статика, снег)", Range(0, 1)) = 0.06
		_NoiseColor ("Noise Color", Color) = (0.8, 0.9, 0.85, 1)
		_NoiseFps ("Noise Rate (смен в сек)", Float) = 30
		_Flicker ("Flicker", Range(0, 0.5)) = 0.05
		_FlickerSpeed ("Flicker Speed", Float) = 30

		// стандартное для UI: маски (Mask) и обрезка
		_StencilComp ("Stencil Comparison", Float) = 8
		_Stencil ("Stencil ID", Float) = 0
		_StencilOp ("Stencil Operation", Float) = 0
		_StencilWriteMask ("Stencil Write Mask", Float) = 255
		_StencilReadMask ("Stencil Read Mask", Float) = 255
		_ColorMask ("Color Mask", Float) = 15
		[Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
	}

	SubShader
	{
		Tags
		{
			"Queue" = "Transparent"
			"IgnoreProjector" = "True"
			"RenderType" = "Transparent"
			"PreviewType" = "Plane"
			"CanUseSpriteAtlas" = "True"
		}

		Stencil
		{
			Ref [_Stencil]
			Comp [_StencilComp]
			Pass [_StencilOp]
			ReadMask [_StencilReadMask]
			WriteMask [_StencilWriteMask]
		}

		Cull Off
		Lighting Off
		ZWrite Off
		ZTest [unity_GUIZTestMode]
		// премультиплицированная альфа: затемнение (скан-линии, виньетка) — через альфу, свечение (полоса, статика) — добавкой
		Blend One OneMinusSrcAlpha
		ColorMask [_ColorMask]

		Pass
		{
			Name "Default"
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma target 2.0

			#include "UnityCG.cginc"
			#include "UnityUI.cginc"

			#pragma multi_compile_local _ UNITY_UI_CLIP_RECT
			#pragma multi_compile_local _ UNITY_UI_ALPHACLIP

			struct appdata_t
			{
				float4 vertex : POSITION;
				float4 color : COLOR;
				float2 texcoord : TEXCOORD0;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct v2f
			{
				float4 vertex : SV_POSITION;
				fixed4 color : COLOR;
				float2 texcoord : TEXCOORD0;
				float4 worldPosition : TEXCOORD1;
				float4 screenPos : TEXCOORD2;
				UNITY_VERTEX_OUTPUT_STEREO
			};

			sampler2D _MainTex;
			fixed4 _Color;
			float4 _ClipRect;

			float _ScanlineCount;
			float _ScanlineIntensity;
			float _ScanlineSpeed;
			float _Vignette;
			float _RollBarIntensity;
			float _RollBarSpeed;
			float _RollBarWidth;
			float _Noise;
			fixed4 _NoiseColor;
			float _NoiseFps;
			float _Flicker;
			float _FlickerSpeed;

			float Hash(float2 p)
			{
				p = frac(p * float2(123.34, 456.21));
				p += dot(p, p + 45.32);
				return frac(p.x * p.y);
			}

			v2f vert(appdata_t v)
			{
				v2f OUT;
				UNITY_SETUP_INSTANCE_ID(v);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
				OUT.worldPosition = v.vertex;
				OUT.vertex = UnityObjectToClipPos(v.vertex);
				OUT.texcoord = v.texcoord;
				OUT.color = v.color * _Color;
				OUT.screenPos = ComputeScreenPos(OUT.vertex);
				return OUT;
			}

			fixed4 frag(v2f IN) : SV_Target
			{
				// координаты экрана, а не Image: полосы и статика привязаны к "кинескопу", а не к прямоугольнику
				float2 uv = IN.screenPos.xy / IN.screenPos.w;
				float time = _Time.y;

				// затемнение: скан-линии + виньетка + мерцание
				float scan = sin((uv.y + time * _ScanlineSpeed) * _ScanlineCount * UNITY_PI) * 0.5 + 0.5;
				float darkness = _ScanlineIntensity * (1.0 - scan);
				float vig = saturate(16.0 * uv.x * uv.y * (1.0 - uv.x) * (1.0 - uv.y));
				darkness = 1.0 - (1.0 - darkness) * pow(vig, _Vignette);
				darkness = 1.0 - (1.0 - darkness) * (1.0 - _Flicker * Hash(float2(floor(time * _FlickerSpeed), 7.0)));

				// свечение: бегущая полоса + статика (меняется скачками Noise Rate раз в секунду)
				float barPos = frac(uv.y + time * _RollBarSpeed);
				float bar = smoothstep(_RollBarWidth, 0.0, abs(barPos - 0.5)) * _RollBarIntensity;
				float2 pixel = floor(uv * _ScreenParams.xy);
				float noise = Hash(pixel + floor(time * _NoiseFps) * 13.7) * _Noise;
				float3 glow = _NoiseColor.rgb * (noise + bar);

				// цвет и альфа Image — общая сила накладки; спрайт (если есть) работает как маска формы
				float strength = IN.color.a * tex2D(_MainTex, IN.texcoord).a;
				fixed4 color = fixed4(glow * IN.color.rgb * strength, saturate(darkness) * strength);

				#ifdef UNITY_UI_CLIP_RECT
				color *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
				#endif
				#ifdef UNITY_UI_ALPHACLIP
				clip(color.a - 0.001);
				#endif

				return color;
			}
			ENDCG
		}
	}
}
