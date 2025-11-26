window.clipboardViewer = (function () {
    let escapeHandlerRegistered = false;
    let dotnetRef = null;

    function registerEscapeHandler(ref) {
        // prevents multiple listeners if already registered
        if (escapeHandlerRegistered) return;

        dotnetRef = ref;
        document.addEventListener('keydown', handleEscape);
        escapeHandlerRegistered = true;
    }

    function handleEscape(e) {
        if (e.key === "Escape") {
            dotnetRef.invokeMethodAsync('OnEscapePressed');
        }
    }

    function unregisterEscapeHandler() {
        if (!escapeHandlerRegistered) return;

        document.removeEventListener('keydown', handleEscape);
        escapeHandlerRegistered = false;
        dotnetRef = null;
    }

    function focusElement(el) {
        if (el) el.focus();
    }

    function focusRow(index) {
        const rows = document.querySelectorAll('.clipboard-entry');
        if (rows && rows[index]) {
            focusElement(rows[index]);
        }
    }

    function focusFirstRow() {
        const first = document.querySelector('.clipboard-entry');
        focusElement(first);
    }

    function focusLastRow() {
        const rows = document.querySelectorAll('.clipboard-entry');
        if (rows.length > 0) {
            focusElement(rows[rows.length - 1]);
        }
    }

    async function copyText(text) {
        try {
            await navigator.clipboard.writeText(text);
            console.log("Text copied to clipboard");
        } catch (err) {
            console.error("Failed to copy text: ", err);
        }
    }

    async function copyImage(imageUrl) {
        try {
            const response = await fetch(imageUrl);
            const blob = await response.blob();
            await navigator.clipboard.write([
                new ClipboardItem({ [blob.type]: blob })
            ]);
            console.log("Image copied to clipboard");
        } catch (err) {
            console.error("Failed to copy image: ", err);
        }
    }

    async function downloadFileFromStream(fileName, contentStreamReference) {
        const arrayBuffer = await contentStreamReference.arrayBuffer();
        const blob = new Blob([arrayBuffer]);
        const url = URL.createObjectURL(blob);
        const anchor = document.createElement('a');
        anchor.href = url;
        anchor.download = fileName || 'download';
        document.body.appendChild(anchor);
        anchor.click();
        anchor.remove();
        URL.revokeObjectURL(url);
    }

    async function readManifest(inputId) {
        const input = document.getElementById(inputId);
        if (!input || !input.files || input.files.length === 0) return null;

        const file = input.files[0];
        const arrayBuffer = await file.arrayBuffer();
        const zip = await JSZip.loadAsync(arrayBuffer);
        const manifestFile = zip.file("manifest.json");
        if (!manifestFile) return null;

        const text = await manifestFile.async("string");
        return JSON.parse(text);
    }

    return {
        registerEscapeHandler,
        unregisterEscapeHandler,
        copyText,
        copyImage,
        focusElement,
        focusRow,
        focusFirstRow,
        focusLastRow,
        downloadFileFromStream,
        readManifest
    };
})();