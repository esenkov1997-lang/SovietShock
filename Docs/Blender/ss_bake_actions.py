# SovietShock — запекание анимаций рук перед экспортом (Blender 4.4+ / 5.x, слотовые экшены).
#
# Зачем: оружие удобно анимировать в его собственном риге (Wrench_SK и т.п.), а руки привязаны к нему
# констрейнтами. Экспортёр Auto-Rig Pro подставляет экшены только рукам — риг оружия остаётся в текущей
# анимации, и во всех клипах получается одно и то же. Поэтому перед экспортом движение запекается в руки.
#
# "Запечь" для каждого экшена:
#   1. ставит его рукам (их слот) и всем остальным объектам, у которых в нём есть слот (оружие, CamTarget...);
#   2. запекает итоговую позу ВСЕХ костей рук (Visual Keying) в новый экшен "<Имя><Суффикс>" с одним слотом рук;
#   3. возвращает всё как было. Исходные экшены не меняются.
# Слот рук определяется по содержимому (в нём ключи на кости выбранного рига), а не по имени слота:
# слот называется по объекту в момент первого ключа и при переименовании объекта не меняется.
#
# Констрейнты на внешние объекты на время экспорта выключаются переключателем самого рига (драйвер) — вручную.
#
# Установка: Edit → Preferences → Add-ons → (стрелка вниз) Install from Disk → этот файл. Либо открыть
# в Text Editor и нажать Run Script (панель появится до перезапуска Blender).
# Панель: 3D Viewport → боковая панель (N) → вкладка "SovietShock".

bl_info = {
    "name": "SovietShock: Bake Arms Actions",
    "author": "SovietShock",
    "version": (2, 0, 0),
    "blender": (4, 4, 0),
    "location": "View3D > Sidebar > SovietShock",
    "description": "Запекание слотовых экшенов (руки + оружие) в риг рук перед экспортом",
    "category": "Animation",
}

import bpy


# ---------- слоты ----------

def is_armature(self, obj):
    return obj is not None and obj.type == 'ARMATURE'


def slot_fcurves(action, slot):
    """F-кривые одного слота экшена (Blender 4.4+)."""
    try:
        from bpy_extras import anim_utils
        channelbag = anim_utils.action_get_channelbag_for_slot(action, slot)
        if channelbag is not None:
            return list(channelbag.fcurves)
    except (ImportError, AttributeError):
        pass
    for layer in getattr(action, "layers", []):
        for strip in layer.strips:
            channelbag = strip.channelbag(slot) if hasattr(strip, "channelbag") else None
            if channelbag is not None:
                return list(channelbag.fcurves)
    return []


def is_arms_slot(action, slot, arms):
    """Слот анимирует кости этого рига — есть хотя бы одна кривая pose.bones["<кость рига>"]."""
    bones = arms.pose.bones
    for fcurve in slot_fcurves(action, slot):
        path = fcurve.data_path
        if path.startswith('pose.bones["'):
            bone_name = path[len('pose.bones["'):].split('"]', 1)[0]
            if bone_name in bones:
                return True
    return False


def arms_slot(action, arms):
    for slot in getattr(action, "slots", []):
        if is_arms_slot(action, slot, arms):
            return slot
    return None


def slot_object_by_name(slot):
    """Объект текущей сцены по имени слота — для остальных объектов (оружие, CamTarget)."""
    if getattr(slot, "target_id_type", 'OBJECT') != 'OBJECT':
        return None
    name = getattr(slot, "name_display", None)
    if not name:
        identifier = getattr(slot, "identifier", "")
        name = identifier[2:] if identifier.startswith("OB") else identifier
    # из сцены, а не из bpy.data: при связанном риге в файле есть ещё исходный объект библиотеки с тем же именем
    return bpy.context.scene.objects.get(name)


def actions_to_bake(settings):
    arms = settings.arms
    result = []
    for action in bpy.data.actions:
        if settings.suffix and action.name.endswith(settings.suffix):
            continue  # уже запечённые
        if settings.name_filter and settings.name_filter.lower() not in action.name.lower():
            continue
        if action.library is not None:
            continue  # экшены из связанных файлов не трогаем
        if arms_slot(action, arms) is not None:
            result.append(action)
    return result


def assign(obj, action, slot):
    if obj.animation_data is None:
        obj.animation_data_create()
    obj.animation_data.action = action
    if action is not None and slot is not None:
        obj.animation_data.action_slot = slot


# ---------- настройки сцены ----------

class SS_BakeSettings(bpy.types.PropertyGroup):
    arms: bpy.props.PointerProperty(
        name="Риг рук", type=bpy.types.Object, poll=is_armature,
        description="Арматура рук, в которую запекается движение (её экспортирует ARP)")
    suffix: bpy.props.StringProperty(
        name="Суффикс", default="_Bake",
        description="Добавляется к имени запечённого экшена: Wrench_Idle → Wrench_Idle_Bake")
    name_filter: bpy.props.StringProperty(
        name="Фильтр", default="",
        description="Запекать только экшены, в имени которых есть этот текст (например, Wrench_). Пусто — все")


# ---------- запекание ----------

class SS_OT_bake_arms_actions(bpy.types.Operator):
    bl_idname = "ss.bake_arms_actions"
    bl_label = "Запечь"
    bl_description = ("Для каждого экшена со слотом рук: поставить его рукам и остальным объектам со слотами "
                      "и запечь итоговую позу всех костей рук в новый экшен с суффиксом")
    bl_options = {'REGISTER', 'UNDO'}

    def execute(self, context):
        settings = context.scene.ss_bake
        arms = settings.arms
        if arms is None:
            self.report({'ERROR'}, "Выбери риг рук в панели")
            return {'CANCELLED'}

        actions = actions_to_bake(settings)
        if not actions:
            self.report({'WARNING'}, "Нет экшенов с ключами на кости выбранного рига рук")
            return {'CANCELLED'}

        # всё, чему будем подставлять экшены, — чтобы потом вернуть как было
        touched = {arms}
        for action in actions:
            for slot in action.slots:
                obj = slot_object_by_name(slot)
                if obj is not None:
                    touched.add(obj)
        original = {}
        for obj in touched:
            data = obj.animation_data
            original[obj] = (data.action, data.action_slot) if data is not None else (None, None)
        original_frame = context.scene.frame_current
        original_active = context.view_layer.objects.active

        baked = []
        try:
            if context.mode != 'OBJECT':
                bpy.ops.object.mode_set(mode='OBJECT')

            for action in actions:
                # 1. рукам — их слот (найден по ключам), остальным объектам — их слоты по имени
                hands_slot = arms_slot(action, arms)
                for slot in action.slots:
                    if slot == hands_slot:
                        continue
                    obj = slot_object_by_name(slot)
                    if obj is not None and obj != arms:
                        assign(obj, action, slot)
                assign(arms, action, hands_slot)

                start, end = (int(round(v)) for v in action.frame_range)

                # 2. запекание всех костей рук: итоговая поза с учётом всех констрейнтов, в новый экшен
                bpy.ops.object.select_all(action='DESELECT')
                arms.select_set(True)
                context.view_layer.objects.active = arms
                bpy.ops.object.mode_set(mode='POSE')
                bpy.ops.nla.bake(
                    frame_start=start, frame_end=end,
                    only_selected=False, visual_keying=True,
                    clear_constraints=False, clear_parents=False,
                    use_current_action=False, clean_curves=False,
                    bake_types={'POSE'})
                bpy.ops.object.mode_set(mode='OBJECT')

                # 3. имя и защита от удаления; старый результат с тем же именем заменяем
                result = arms.animation_data.action
                target_name = action.name + settings.suffix
                old = bpy.data.actions.get(target_name)
                if old is not None and old != result:
                    bpy.data.actions.remove(old)
                result.name = target_name
                result.use_fake_user = True
                result.use_frame_range = True
                result.frame_range = (start, end)
                baked.append(target_name)
        finally:
            # 4. возвращаем как было
            if context.mode != 'OBJECT':
                bpy.ops.object.mode_set(mode='OBJECT')
            for obj, (action, slot) in original.items():
                if obj.animation_data is not None:
                    obj.animation_data.action = action
                    if action is not None and slot is not None:
                        obj.animation_data.action_slot = slot
            context.scene.frame_set(original_frame)
            if original_active is not None:
                context.view_layer.objects.active = original_active

        self.report({'INFO'}, f"Запечено: {len(baked)} — {', '.join(baked)}")
        return {'FINISHED'}


# ---------- панель ----------

class SS_PT_bake_panel(bpy.types.Panel):
    bl_label = "Запекание рук"
    bl_idname = "SS_PT_bake_panel"
    bl_space_type = 'VIEW_3D'
    bl_region_type = 'UI'
    bl_category = "SovietShock"

    def draw(self, context):
        layout = self.layout
        settings = context.scene.ss_bake

        layout.prop(settings, "arms")
        layout.prop(settings, "suffix")
        layout.prop(settings, "name_filter")

        if settings.arms is not None:
            actions = actions_to_bake(settings)
            box = layout.box()
            box.label(text=f"Будет запечено: {len(actions)}")
            for action in actions[:12]:
                hands_slot = arms_slot(action, settings.arms)
                others = [s.name_display for s in action.slots if s != hands_slot]
                text = f"{action.name}  (руки: {hands_slot.name_display}"
                text += f"; ещё: {', '.join(others)})" if others else ")"
                box.label(text=text, icon='ACTION')
            if len(actions) > 12:
                box.label(text=f"... и ещё {len(actions) - 12}")

        layout.operator(SS_OT_bake_arms_actions.bl_idname, icon='RENDER_ANIMATION')


classes = (
    SS_BakeSettings,
    SS_OT_bake_arms_actions,
    SS_PT_bake_panel,
)


def register():
    for cls in classes:
        bpy.utils.register_class(cls)
    bpy.types.Scene.ss_bake = bpy.props.PointerProperty(type=SS_BakeSettings)


def unregister():
    del bpy.types.Scene.ss_bake
    for cls in reversed(classes):
        bpy.utils.unregister_class(cls)


if __name__ == "__main__":
    register()
