#target photoshop

// 由 Unity 生成的 job.jsx 调用；只修改 duplicate 文档，绝不保存源 PSD。
var PsdBakeEffects = (function () {
    function quote(s) {
        return '"' + String(s).replace(/\\/g, '\\\\').replace(/"/g, '\\"')
            .replace(/\r/g, '\\r').replace(/\n/g, '\\n').replace(/\t/g, '\\t') + '"';
    }
    function json(v) {
        if (v === null) return 'null';
        if (typeof v === 'string') return quote(v);
        if (typeof v === 'number' || typeof v === 'boolean') return String(v);
        var a = [], k;
        if (v instanceof Array) { for (k = 0; k < v.length; k++) a.push(json(v[k])); return '[' + a.join(',') + ']'; }
        for (k in v) if (v.hasOwnProperty(k)) a.push(quote(k) + ':' + json(v[k]));
        return '{' + a.join(',') + '}';
    }
    function write(path, value) {
        var file = new File(path); file.encoding = 'UTF8';
        if (!file.open('w')) throw Error('Cannot write result: ' + path);
        file.write(json(value)); file.close();
    }
    function findPath(parent, id, path) {
        for (var i = 0; i < parent.layers.length; i++) {
            var layer = parent.layers[i], next = path.concat([i]);
            if (layer.id === id) return next;
            if (layer.typename === 'LayerSet') { var nested = findPath(layer, id, next); if (nested) return nested; }
        }
        return null;
    }
    function atPath(doc, path) {
        var layer = doc;
        for (var i = 0; i < path.length; i++) layer = layer.layers[path[i]];
        return layer;
    }
    function bounds(layer) {
        var b = layer.bounds;
        return [Math.floor(b[0].as('px')), Math.floor(b[1].as('px')), Math.ceil(b[2].as('px')), Math.ceil(b[3].as('px'))];
    }
    function prepareDocument(doc) {
        app.activeDocument = doc;
        // 副本可能继承源稿中选中的蒙版/Alpha 通道；所有后续操作明确作用于 RGB 合成通道。
        doc.activeChannels = doc.componentChannels;
        doc.selection.deselect();
    }
    function run(job) {
        var result = { success: false, error: '', entries: [] };
        var source = null, temporary = null, opened = false, previous = app.documents.length ? app.activeDocument : null;
        var dialogs = app.displayDialogs, units = app.preferences.rulerUnits;
        var stage = 'open source', layerId = null, layerName = '';
        try {
            app.displayDialogs = DialogModes.NO; app.preferences.rulerUnits = Units.PIXELS;
            var file = new File(job.sourcePath);
            if (!file.exists) throw Error('Source PSD does not exist');
            for (var d = 0; d < app.documents.length; d++) {
                try { if (app.documents[d].fullName.fsName === file.fsName) { source = app.documents[d]; break; } } catch (ignored) { }
            }
            if (source && !source.saved) throw Error('Source PSD has unsaved edits. Save it in Photoshop before exporting.');
            if (!source) { source = app.open(file); opened = true; }
            var paths = [];
            for (var i = 0; i < job.layerIds.length; i++) {
                var path = findPath(source, job.layerIds[i], []);
                if (!path) throw Error('Layer ID not found: ' + job.layerIds[i]);
                paths.push(path);
            }
            for (var n = 0; n < paths.length; n++) {
                layerId = job.layerIds[n]; layerName = ''; stage = 'duplicate source';
                app.activeDocument = source;
                temporary = source.duplicate('__PsdToolFx_' + job.layerIds[n], false);
                prepareDocument(temporary);
                var target = atPath(temporary, paths[n]);
                layerName = target.name; stage = 'isolate target';
                // 只解锁临时副本中的祖先，避免锁定组阻止将目标提升到根层级。
                for (var parent = target.parent; parent.typename !== 'Document'; parent = parent.parent) parent.allLocked = false;
                target.allLocked = false;
                if (target.parent.typename !== 'Document') target.move(temporary, ElementPlacement.PLACEATBEGINNING);
                // DOM remove() 会隐式执行“选择图层”，在某些文档状态下不可用。
                // 隐藏其余根图层即可隔离像素，不需要删除、选中那些图层。
                var targetId = target.id;
                for (var j = 0; j < temporary.layers.length; j++)
                    temporary.layers[j].visible = temporary.layers[j].id === targetId;
                target.allLocked = false; target.visible = true;
                temporary.activeLayer = target;
                // 层不透明度留给 Unity；填充不透明度与 FX 之间的关系由 PS 烘焙。
                target.opacity = 100;
                stage = 'expand canvas';
                var b = bounds(target), w = temporary.width.as('px'), h = temporary.height.as('px');
                var padding = Math.ceil(Math.max(64, -b[0] + 16, -b[1] + 16, b[2] - w + 16, b[3] - h + 16));
                temporary.resizeCanvas(UnitValue(w + padding * 2, 'px'), UnitValue(h + padding * 2, 'px'), AnchorPosition.MIDDLECENTER);
                var blank = temporary.artLayers.add(); blank.name = '__TransparentMerge'; blank.visible = true;
                stage = 'merge visible FX';
                temporary.mergeVisibleLayers();
                var raster = temporary.activeLayer;
                var rendered = bounds(raster);
                if (rendered[2] <= rendered[0] || rendered[3] <= rendered[1]) throw Error('Rendered layer is empty: ' + job.layerIds[n]);
                var left = rendered[0] - padding, top = rendered[1] - padding;
                stage = 'crop rendered pixels';
                temporary.crop([UnitValue(rendered[0], 'px'), UnitValue(rendered[1], 'px'), UnitValue(rendered[2], 'px'), UnitValue(rendered[3], 'px')]);
                var destination = job.outputFolder + '/' + job.layerIds[n] + '.png';
                var options = new PNGSaveOptions(); options.compression = 6; options.interlaced = false;
                stage = 'save PNG';
                temporary.saveAs(new File(destination), options, true, Extension.LOWERCASE);
                result.entries.push({ layerId: job.layerIds[n], path: destination, x: left, y: top,
                    width: Math.round(temporary.width.as('px')), height: Math.round(temporary.height.as('px')) });
                temporary.close(SaveOptions.DONOTSAVECHANGES); temporary = null;
            }
            result.success = true;
        } catch (e) {
            result.error = 'Layer ' + layerId + ' [' + layerName + '], ' + stage + ': ' + String(e) + ' (line ' + e.line + ')';
        } finally {
            if (temporary) try { temporary.close(SaveOptions.DONOTSAVECHANGES); } catch (ignored1) { }
            if (opened && source) try { source.close(SaveOptions.DONOTSAVECHANGES); } catch (ignored2) { }
            if (previous) try { app.activeDocument = previous; } catch (ignored3) { }
            app.preferences.rulerUnits = units; app.displayDialogs = dialogs;
            write(job.resultPath, result);
        }
    }
    return { run: run };
}());
