// Размытие экрана (Dual Kawase): цепочка уменьшений картинки и обратных увеличений — мягкое, дешёвое
// и без "квадратиков" даже при сильном размытии. На последнем проходе — опциональное ретро: пикселизация
// (картинка берётся блоками) и урезанная палитра (меньше уровней яркости на канал). Рисует его HUD.ScreenBlurFeature (Renderer Feature),
// силу и настройки берёт из Volume-компонента HUD.ScreenBlur. Вручную материал не нужен.
Shader "Hidden/SovietShock/ScreenBlur"
{
	SubShader
	{
		Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
		ZWrite Off ZTest Always Cull Off

		HLSLINCLUDE
		#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
		#include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

		// xy — половина текселя исходной текстуры, умноженная на Spread
		float4 _ScreenBlurOffset;
		// 0..1 — насколько размытая картинка перекрывает исходную (последний проход)
		float _ScreenBlurIntensity;
		// ретро: xy — блоков по ширине/высоте, z — пикселизация вкл (1) / выкл (0), w — уровней на канал (0 — выкл)
		float4 _ScreenBlurPixelate;

		half4 SampleSource(float2 uv)
		{
			return SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, 0);
		}

		half4 FragDown(Varyings input) : SV_Target
		{
			UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
			float2 uv = input.texcoord;
			float2 o = _ScreenBlurOffset.xy;

			half4 color = SampleSource(uv) * 4.0;
			color += SampleSource(uv + float2(-o.x, -o.y));
			color += SampleSource(uv + float2( o.x, -o.y));
			color += SampleSource(uv + float2(-o.x,  o.y));
			color += SampleSource(uv + float2( o.x,  o.y));
			return color / 8.0;
		}

		half4 Up(float2 uv)
		{
			float2 o = _ScreenBlurOffset.xy;

			half4 color = SampleSource(uv + float2(-o.x * 2.0, 0.0));
			color += SampleSource(uv + float2( o.x * 2.0, 0.0));
			color += SampleSource(uv + float2(0.0, -o.y * 2.0));
			color += SampleSource(uv + float2(0.0,  o.y * 2.0));
			color += SampleSource(uv + float2(-o.x,  o.y)) * 2.0;
			color += SampleSource(uv + float2( o.x,  o.y)) * 2.0;
			color += SampleSource(uv + float2(-o.x, -o.y)) * 2.0;
			color += SampleSource(uv + float2( o.x, -o.y)) * 2.0;
			return color / 12.0;
		}

		half4 FragUp(Varyings input) : SV_Target
		{
			UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
			return Up(input.texcoord);
		}

		// последнее увеличение сразу ложится на кадр камеры: альфа = сила, альфа самого кадра не трогается
		half4 FragComposite(Varyings input) : SV_Target
		{
			UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
			float2 uv = input.texcoord;

			// пикселизация: все экранные пиксели блока берут цвет из его центра
			if (_ScreenBlurPixelate.z > 0.5)
				uv = (floor(uv * _ScreenBlurPixelate.xy) + 0.5) / _ScreenBlurPixelate.xy;

			half3 color = Up(uv).rgb;

			// палитра режется в гамма-пространстве — так ступени яркости ровные на глаз;
			// кадр здесь ещё в HDR (до тонмаппинга), поэтому значения > 1 не обрезаются
			if (_ScreenBlurPixelate.w > 0.5)
			{
				half steps = _ScreenBlurPixelate.w;
				half3 gamma = pow(max(color, 0.0), 1.0 / 2.2);
				gamma = floor(gamma * steps + 0.5) / steps;
				color = pow(gamma, 2.2);
			}

			return half4(color, _ScreenBlurIntensity);
		}
		ENDHLSL

		Pass
		{
			Name "Down"
			HLSLPROGRAM
			#pragma vertex Vert
			#pragma fragment FragDown
			ENDHLSL
		}

		Pass
		{
			Name "Up"
			HLSLPROGRAM
			#pragma vertex Vert
			#pragma fragment FragUp
			ENDHLSL
		}

		Pass
		{
			Name "Composite"
			Blend SrcAlpha OneMinusSrcAlpha, Zero One
			HLSLPROGRAM
			#pragma vertex Vert
			#pragma fragment FragComposite
			ENDHLSL
		}
	}
}
