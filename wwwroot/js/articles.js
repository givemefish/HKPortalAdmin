/* global kendo, jQuery */
(function ($) {
    "use strict";
    const config = JSON.parse(document.getElementById("portalConfig").textContent);
    const t = key => config.text[key] || key;
    if (!window.kendo || !$.fn.kendoGrid || !$.fn.kendoEditor) return;
    $("#kendoMissing").hide();
    kendo.culture(config.culture);
    const langs = ["ZHTW", "ZHCN", "EN"];
    const maxSize = 10 * 1024 * 1024;
    const statusItems = ["Pending", "Published", "Scheduled", "Offline", "Invalid"].map(value => ({ value, text: t(value) }));
    const actions = ["Pending", "Publish", "Offline"].map(value => ({ value, text: t(value) }));
    let current = null, readOnly = false, busy = false, dirty = false, allowClose = false;
    let pendingFiles = [], removed = new Set(), categoryData = [];
    const csrf = () => $("input[name='__RequestVerificationToken']").first().val();
    const encode = value => kendo.htmlEncode(value == null ? "" : String(value));
    function notice(message, error = false, editor = false) {
        $(editor ? "#editorNotice" : "#notice").removeClass("d-none alert-danger alert-success")
            .addClass(error ? "alert-danger" : "alert-success").text(message);
    }
    async function request(url, options = {}) {
        const response = await fetch(url, { credentials: "same-origin", ...options,
            headers: { "X-CSRF-TOKEN": csrf(), ...options.headers } });
        const body = await response.json().catch(() => ({}));
        if (!response.ok) throw new Error(body.error || (response.status === 401 ? t("Unauthenticated") : t("UnexpectedError")));
        return body;
    }
    const queryUrl = (url, params) => url + "?" + new URLSearchParams(params).toString();
    function dateText(value) { return value ? kendo.toString(kendo.parseDate(value), "yyyy/MM/dd HH:mm") : "—"; }
    function localDate(value, end) {
        if (!value) return null;
        const adjusted = new Date(value);
        adjusted.setSeconds(end ? 59 : 0, 0);
        return kendo.toString(adjusted, "yyyy-MM-ddTHH:mm:ss");
    }
    function setBusy(value) {
        busy = value;
        document.getElementById("articleForm").inert = value;
        $("#articleForm").attr("aria-busy", String(value));
        $("#saveArticle,#cancelEdit,#newArticle,#applyState,#deleteArticles").prop("disabled", value);
        $("#articleWindow .image-input,#articleWindow .attachment-input").prop("disabled", value || readOnly);
    }
    const filterCategory = $("#filterCategory").kendoDropDownTree({
        dataTextField: "text", dataValueField: "id", placeholder: t("AllCategories"), filter: "contains",
        clearButton: true, autoClose: true, dataSource: []
    }).data("kendoDropDownTree");
    const editCategory = $("#editCategory").kendoDropDownTree({
        dataTextField: "text", dataValueField: "id", placeholder: t("Category"), filter: "contains",
        clearButton: true, autoClose: true, dataSource: [], change: () => { dirty = true; }
    }).data("kendoDropDownTree");
    const filterStatus = $("#filterStatus").kendoDropDownList({
        dataTextField: "text", dataValueField: "value", optionLabel: t("AllStatuses"), dataSource: statusItems
    }).data("kendoDropDownList");
    const batchState = $("#batchState").kendoDropDownList({
        dataTextField: "text", dataValueField: "value", dataSource: actions
    }).data("kendoDropDownList");
    const dateOptions = { format: "yyyy/MM/dd HH:mm", timeFormat: "HH:mm", interval: 1,
        parseFormats: ["yyyy/MM/dd HH:mm", "yyyy-MM-ddTHH:mm:ss"], min: new Date(1753, 0, 1),
        change: () => { dirty = true; } };
    const begin = $("#beginDate").kendoDateTimePicker(dateOptions).data("kendoDateTimePicker");
    const end = $("#endDate").kendoDateTimePicker(dateOptions).data("kendoDateTimePicker");
    $("#languageTabs").kendoTabStrip({ animation: false, activate: () => kendo.resize($("#articleWindow")) });
    langs.forEach(lang => $("#body" + lang).kendoEditor({
        tools: ["bold", "italic", "underline", "strikethrough", "justifyLeft", "justifyCenter", "justifyRight",
            "insertUnorderedList", "insertOrderedList", "indent", "outdent", "createLink", "unlink", "insertImage",
            "createTable", "formatting", "fontName", "fontSize", "foreColor", "backColor", "cleanFormatting", "viewHtml"],
        change: () => { dirty = true; }
    }));
    langs.forEach(lang => $( $("#body" + lang).data("kendoEditor").body ).on("input", () => { dirty = true; }));
    const windowWidget = $("#articleWindow").kendoWindow({
        title: t("Edit"), modal: true, visible: false, width: 1100, resizable: true, actions: ["Maximize", "Close"],
        close: e => {
            if (busy || (!allowClose && dirty && !readOnly && !window.confirm(t("DiscardConfirm")))) e.preventDefault();
            else dirty = false;
            allowClose = false;
        }
    }).data("kendoWindow");
    const grid = $("#articleGrid").kendoGrid({
        autoBind: false, sortable: true, scrollable: true,
        dataSource: {
            transport: { read: async options => {
                const sort = (options.data.sort || [])[0] || {};
                const params = { page: options.data.page || 1, pageSize: options.data.pageSize || 10,
                    keyword: $("#keyword").val(), status: filterStatus.value(), sortField: sort.field || "bdModDate", sortDirection: sort.dir || "desc" };
                const category = filterCategory.value();
                if (category !== "" && category != null) params.cateId = category;
                try { options.success(await request(queryUrl(config.urls.list, params))); }
                catch (error) { options.error(error); notice(error.message, true); }
            } },
            schema: { data: "data", total: "total", model: { id: "uid", fields: {
                uid: { type: "string" }, subject: { type: "string" }, postDate: { type: "date" },
                bdModDate: { type: "date" }, publishBgnDate: { type: "date" }, publishEndDate: { type: "date" }
            } } },
            serverPaging: true, serverSorting: true, pageSize: 10
        },
        pageable: { pageSizes: [10, 20, 50, 100], buttonCount: 5, messages: {
            display: t("PageDisplay"), empty: t("PageEmpty"), itemsPerPage: t("PageSize"),
            first: "«", previous: "‹", next: "›", last: "»", refresh: t("Search")
        } },
        noRecords: { template: t("NoRecords") },
        columns: [
            { selectable: true, width: 45 },
            { field: "status", title: t("Status"), width: 125, sortable: false,
                template: row => `<span class="status-badge status-${encode(row.status)}">${encode(t(row.status))}</span>` },
            { field: "categoryNames", title: t("Category"), width: 220, sortable: false, encoded: true },
            { field: "subject", title: t("Subject"), width: 350, encoded: true },
            { field: "postDate", title: t("PostDate"), width: 155, template: row => dateText(row.postDate) },
            { field: "bdModDate", title: t("ModifiedAt"), width: 155, template: row => dateText(row.bdModDate) },
            { field: "bdModUserId", title: t("ModifiedBy"), width: 100, sortable: false, encoded: true },
            { title: t("Actions"), width: 250, template: () => `<div class="row-actions"><button class="btn btn-sm btn-outline-secondary row-view">${encode(t("View"))}</button><button class="btn btn-sm btn-outline-primary row-edit">${encode(t("Edit"))}</button><button class="btn btn-sm btn-outline-danger row-delete">${encode(t("Delete"))}</button><select class="form-select form-select-sm state-select" aria-label="${encode(t("Status"))}"><option value="">${encode(t("Status"))}</option>${actions.map(a => `<option value="${a.value}">${encode(a.text)}</option>`).join("")}</select></div>` }
        ]
    }).data("kendoGrid");
    function refresh() { grid.clearSelection(); return grid.dataSource.read(); }
    function references(rows) { return rows.map(row => ({ uid: row.uid, modifiedToken: row.modifiedToken })); }
    function selected() { return grid.select().toArray().map(row => grid.dataItem(row)); }
    async function batch(action, rows) {
        if (busy) return;
        if (!rows.length) { notice(t("SelectArticles"), true); return; }
        if (!window.confirm(t(action === "Delete" ? "DeleteConfirm" : "StateConfirm"))) return;
        setBusy(true);
        try {
            await request(config.urls.batch, { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ action, articles: references(rows) }) });
            notice(t("Updated"));
            await refresh();
        } catch (error) { notice(error.message, true); }
        finally { setBusy(false); }
    }
    function renderAttachments() {
        langs.forEach(lang => {
            const list = $("#attachments" + lang).empty();
            // Legacy null/unknown language remains visible in the Traditional tab, without rewriting it.
            (current?.attachments || []).filter(a => (langs.includes(a.fileLang) ? a.fileLang : "ZHTW") === lang && !removed.has(a.attID)).forEach(a => {
                const row = $("<li class='list-group-item'>");
                $("<a class='file-name'>").text(a.fileName).attr("href", queryUrl(config.urls.download, { id: current.uid, attachmentId: a.attID })).appendTo(row);
                if (!readOnly) $("<button type='button' class='btn btn-sm btn-outline-danger'>").text(t("Remove")).on("click", () => { removed.add(a.attID); dirty = true; renderAttachments(); }).appendTo(row);
                list.append(row);
            });
            pendingFiles.filter(a => a.lang === lang).forEach(a => {
                const row = $("<li class='list-group-item'>");
                $("<span class='file-name'>").text(a.file.name + " · " + t("NewFile")).appendTo(row);
                $("<button type='button' class='btn btn-sm btn-outline-danger'>").text(t("Remove")).on("click", () => { pendingFiles = pendingFiles.filter(x => x !== a); dirty = true; renderAttachments(); }).appendTo(row);
                list.append(row);
            });
            if (!list.children().length) list.append($("<li class='list-group-item text-secondary'>").text(t("NoAttachments")));
        });
    }
    async function openArticle(id, viewOnly = false) {
        if (busy) return;
        setBusy(true);
        try {
            const article = id ? await request(queryUrl(config.urls.get, { id })) : null;
            const defaults = article ? null : await request(config.urls.defaults);
            current = article; readOnly = viewOnly; dirty = false; pendingFiles = []; removed = new Set();
            $("#editorNotice").addClass("d-none");
            editCategory.value(article?.categoryIds?.[0] ?? ""); editCategory.enable(!readOnly);
            begin.value(kendo.parseDate(article?.prePublishBgnDate || defaults?.begin));
            end.value(kendo.parseDate(article?.prePublishEndDate || defaults?.end));
            begin.enable(!readOnly); end.enable(!readOnly);
            const subjects = { ZHTW: article?.subjectZHTW ?? article?.subject ?? "", ZHCN: article?.subjectZHCN ?? "", EN: article?.subjectEN ?? "" };
            const bodies = { ZHTW: article?.bodyZHTW ?? article?.body ?? "", ZHCN: article?.bodyZHCN ?? "", EN: article?.bodyEN ?? "" };
            langs.forEach(lang => {
                $("#subject" + lang).val(subjects[lang]).prop("readonly", readOnly);
                const editor = $("#body" + lang).data("kendoEditor");
                editor.value(bodies[lang]);
                $(editor.body).attr("contenteditable", String(!readOnly));
                editor.wrapper.find(".k-editor-toolbar").toggle(!readOnly);
            });
            $(".image-controls,.attachment-controls,.save-hint,#saveArticle").toggle(!readOnly);
            renderAttachments();
            $("#languageTabs").data("kendoTabStrip").select(0);
            windowWidget.title(t(viewOnly ? "View" : id ? "Edit" : "New"));
            windowWidget.center().open(); kendo.resize($("#articleWindow"));
            if ((article?.categoryIds?.length || 0) > 1) {
                notice(t("MultipleCategories"), true, true);
                $("#saveArticle").hide();
            }
        } catch (error) { notice(error.message, true); }
        finally { setBusy(false); }
    }
    $("#articleForm").on("input", ".subject-input", () => { dirty = true; });
    $("#articleForm").on("submit", async event => {
        event.preventDefault();
        if (busy || readOnly) return;
        const cate = editCategory.value(), start = begin.value(), finish = end.value();
        if (cate === "" || cate == null) { notice(t("CategoryRequired"), true, true); return; }
        if (!start || !finish || start > finish) { notice(t("InvalidDates"), true, true); return; }
        if (!$("#subjectZHTW").val().trim()) { notice(t("InvalidTitle"), true, true); return; }
        const payload = { uid: current?.uid || null, modifiedToken: current?.modifiedToken || null, cateId: Number(cate),
            prePublishBgnDate: localDate(start, false), prePublishEndDate: localDate(finish, true), removedAttachments: [...removed] };
        langs.forEach(lang => {
            payload["subject" + lang] = $("#subject" + lang).val();
            payload["body" + lang] = $("#body" + lang).data("kendoEditor").value();
        });
        const form = new FormData(); form.append("payload", JSON.stringify(payload));
        pendingFiles.forEach(item => form.append(item.lang, item.file, item.file.name));
        setBusy(true);
        try {
            await request(config.urls.save, { method: "POST", body: form });
            dirty = false; allowClose = true; setBusy(false); windowWidget.close();
            notice(t("Saved")); await refresh();
        } catch (error) { notice(error.message, true, true); }
        finally { setBusy(false); }
    });
    $(".attachment-input").on("change", function () {
        if (busy || readOnly) { this.value = ""; return; }
        const files = [...this.files];
        if (files.some(file => !file.size || file.size > maxSize)) notice(t("FileSize"), true, true);
        else if (files.length + pendingFiles.length > 20) notice(t("InvalidInput"), true, true);
        else { files.forEach(file => pendingFiles.push({ file, lang: this.dataset.lang })); dirty = true; renderAttachments(); }
        this.value = "";
    });
    $(".image-input").on("change", async function () {
        const file = this.files[0], lang = this.dataset.lang; this.value = "";
        if (!file || busy || readOnly) return;
        if (!file.size || file.size > maxSize) { notice(t("FileSize"), true, true); return; }
        const form = new FormData(); form.append("file", file, file.name);
        setBusy(true);
        try {
            const uploaded = await request(config.urls.image, { method: "POST", body: form });
            const editor = $("#body" + lang).data("kendoEditor");
            editor.exec("insertHtml", { value: `<img src="${encode(uploaded.url)}" alt="${encode(file.name)}" />` });
            dirty = true;
        } catch (error) { notice(error.message, true, true); }
        finally { setBusy(false); }
    });
    $("#cancelEdit").on("click", () => windowWidget.close());
    $("#newArticle").on("click", () => openArticle(null));
    $("#applyState").on("click", () => batch(batchState.value(), selected()));
    $("#deleteArticles").on("click", () => batch("Delete", selected()));
    $("#articleGrid").on("click", ".row-edit,.row-view,.row-delete", function () {
        const row = grid.dataItem($(this).closest("tr"));
        if ($(this).hasClass("row-delete")) batch("Delete", [row]);
        else openArticle(row.uid, $(this).hasClass("row-view"));
    }).on("change", ".state-select", function () {
        const action = this.value; this.value = "";
        if (action) batch(action, [grid.dataItem($(this).closest("tr"))]);
    });
    $("#searchForm").on("submit", event => { event.preventDefault(); grid.clearSelection(); grid.dataSource.page(1); });
    $("#reset").on("click", () => { filterCategory.value(""); filterStatus.value(""); $("#keyword").val(""); grid.clearSelection(); grid.dataSource.page(1); });
    window.addEventListener("beforeunload", event => { if (dirty && !readOnly) { event.preventDefault(); event.returnValue = ""; } });
    (async () => {
        try {
            categoryData = await request(config.urls.categories);
            const tree = data => new kendo.data.HierarchicalDataSource({ data, schema: { model: { id: "id", children: "items" } } });
            filterCategory.setDataSource(tree(categoryData)); editCategory.setDataSource(tree(categoryData));
            await refresh();
        } catch (error) { notice(error.message, true); }
    })();
})(jQuery);
