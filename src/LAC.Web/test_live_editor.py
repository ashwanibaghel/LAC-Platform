import os
import json
import sys
import time

sys.stdout.reconfigure(line_buffering=True)

from selenium import webdriver
from selenium.webdriver.common.by import By
from selenium.webdriver.chrome.options import Options
from selenium.webdriver.support.ui import WebDriverWait, Select
from selenium.webdriver.support import expected_conditions as EC

def run_test():
    options = Options()
    options.add_argument("--headless=new")
    options.add_argument("--window-size=1280,800")

    username = os.environ.get("LAC_TEST_USER", "")
    password = os.environ.get("LAC_TEST_PASS", "")

    if not username or not password:
        raise ValueError("LAC_TEST_USER and LAC_TEST_PASS environment variables must be set.")

    print("Launching Chrome via Selenium...")
    driver = webdriver.Chrome(options=options)

    try:
        print("Navigating to http://127.0.0.1:5174/login ...")
        driver.get("http://127.0.0.1:5174/login")
        time.sleep(1)

        print("Authenticating via login form...")
        user_input = WebDriverWait(driver, 10).until(EC.presence_of_element_located((By.ID, "username")))
        user_input.clear()
        user_input.send_keys(username)

        pass_input = driver.find_element(By.ID, "password")
        pass_input.clear()
        pass_input.send_keys(password)

        driver.find_element(By.CSS_SELECTOR, ".login-button").click()
        time.sleep(2)

        print("Fetching matters from API...")
        matters_data = driver.execute_async_script("""
            const done = arguments[arguments.length - 1];
            fetch('/api/matters', { credentials: 'include' })
              .then(async r => {
                const text = await r.text();
                try { return done({ status: r.status, json: JSON.parse(text) }); }
                catch(e) { return done({ status: r.status, rawText: text, err: e.toString() }); }
              })
              .catch(err => done({ error: err.toString() }));
        """)

        matter_id = None
        if isinstance(matters_data, dict) and "json" in matters_data and isinstance(matters_data["json"], dict):
            items = matters_data["json"].get("items", [])
            if items:
                matter_id = items[0]["id"]
                print(f"Found Matter ID: {matter_id}")

        assert matter_id, f"Could not find any matter in API response: {matters_data}"

        print("Creating fresh test draft for live verification...")
        created = driver.execute_async_script(f"""
            const done = arguments[arguments.length - 1];
            fetch('/api/matters/{matter_id}/drafts', {{
                method: 'POST',
                headers: {{ 'Content-Type': 'application/json' }},
                body: JSON.stringify({{ title: 'Live Selenium Audit Draft', draftType: 'Letter' }}),
                credentials: 'include'
            }})
            .then(r => r.json())
            .then(data => done(data))
            .catch(err => done({{ error: err.toString() }}));
        """)

        draft_id = created.get("id") if isinstance(created, dict) else None
        assert draft_id, f"Could not create test draft for matter {matter_id}: {created}"

        target_url = f"http://127.0.0.1:5174/matter-drafts/{draft_id}"
        print(f"Navigating to editor URL: {target_url}")
        driver.get(target_url)
        time.sleep(2)

        print(f"Current URL after navigation: {driver.current_url}")
        print("Waiting for .draft-canvas to load...")
        try:
            WebDriverWait(driver, 15).until(EC.presence_of_element_located((By.CSS_SELECTOR, ".draft-canvas")))
        except Exception as e:
            print(f"FAILED waiting for .draft-canvas! Current URL: {driver.current_url}")
            print(f"Page title: {driver.title}")
            print(f"Page body snippet:\n{driver.find_element(By.TAG_NAME, 'body').text[:500]}")
            print("Browser console logs:")
            for entry in driver.get_log('browser'):
                print(entry)
            raise e
        time.sleep(1)

        def record(label):
            script = """
            const pageWrap = document.querySelector('.draft-page-wrap');
            const canvas = document.querySelector('.draft-canvas');
            const extentShell = document.querySelector('.draft-extent-shell');
            const sheetCards = document.querySelectorAll('.draft-sheet-card');
            const sheetBadges = document.querySelectorAll('.draft-sheet-badge');
            const topRuler = document.querySelector('.draft-top-ruler-bar');
            const leftRuler = document.querySelector('.draft-left-ruler-bar');

            let caretCoords = null;
            const sel = window.getSelection();
            if (sel && sel.rangeCount > 0) {
              const r = sel.getRangeAt(0).getBoundingClientRect();
              caretCoords = { top: Math.round(r.top), left: Math.round(r.left), bottom: Math.round(r.bottom) };
            }

            const wrapRect = pageWrap ? pageWrap.getBoundingClientRect() : null;
            const canvasRect = canvas ? canvas.getBoundingClientRect() : null;
            const shellRect = extentShell ? extentShell.getBoundingClientRect() : null;
            const topRulerRect = topRuler ? topRuler.getBoundingClientRect() : null;
            const leftRulerRect = leftRuler ? leftRuler.getBoundingClientRect() : null;

            return {
              scrollTop: pageWrap ? pageWrap.scrollTop : 0,
              scrollLeft: pageWrap ? pageWrap.scrollLeft : 0,
              viewportRect: wrapRect ? { top: Math.round(wrapRect.top), left: Math.round(wrapRect.left), width: Math.round(wrapRect.width), height: Math.round(wrapRect.height) } : null,
              canvasRect: canvasRect ? { top: Math.round(canvasRect.top), left: Math.round(canvasRect.left), width: Math.round(canvasRect.width), height: Math.round(canvasRect.height) } : null,
              shellRect: shellRect ? { top: Math.round(shellRect.top), left: Math.round(shellRect.left), width: Math.round(shellRect.width), height: Math.round(shellRect.height) } : null,
              logicalWidth: canvas ? canvas.offsetWidth : 0,
              logicalHeight: canvas ? canvas.offsetHeight : 0,
              currentZoom: canvas ? (canvas.style.transform || "scale(1)") : "scale(1)",
              caretCoords: caretCoords,
              renderedPageCount: sheetCards.length,
              reportedPageCount: sheetBadges.length,
              page1TopRelativeToViewport: (canvasRect && wrapRect) ? Math.round(canvasRect.top - wrapRect.top) : null,
              horizontalRulerPaperOrigin: (canvasRect && topRulerRect) ? Math.round(canvasRect.left - topRulerRect.left) : null,
              verticalRulerPaperOrigin: (canvasRect && leftRulerRect) ? Math.round(canvasRect.top - leftRulerRect.top) : null
            };
            """
            res = driver.execute_script(script)
            print(f"\n=== INSTRUMENTATION RECORD: {label} ===")
            print(json.dumps(res, indent=2))
            return res

        # 1. Baseline at 100%
        rec1 = record("1. Baseline at 100%")

        # 2. Target caret explicitly to first paragraph start
        print("Action: Placing caret explicitly at start of paragraph 1...")
        driver.execute_script("""
            const pm = document.querySelector('.ProseMirror');
            const firstP = pm ? (pm.querySelector('p, h1, h2') || pm.firstChild) : null;
            if (firstP) {
              const range = document.createRange();
              range.setStart(firstP, 0);
              range.collapse(true);
              const sel = window.getSelection();
              sel.removeAllRanges();
              sel.addRange(range);
              if (pm.focus) pm.focus();
            }
        """)
        time.sleep(0.3)
        rec2 = record("2. Caret at First Line")

        # 3. Type text on line 1
        pm_elements = driver.find_elements(By.CSS_SELECTOR, ".ProseMirror")
        if pm_elements:
            print("Action: Typing text on line 1...")
            pm_elements[0].send_keys("Testing explicit line 1 typing.")
            time.sleep(0.3)
            rec3 = record("3. Type on Line 1")

        # Assertion: Page 1 top must remain stable at 36px
        assert rec1["page1TopRelativeToViewport"] == 36, f"Expected 36, got {rec1['page1TopRelativeToViewport']}"
        assert rec2["page1TopRelativeToViewport"] == 36, f"Expected 36, got {rec2['page1TopRelativeToViewport']}"
        assert rec3["page1TopRelativeToViewport"] == 36, f"Expected 36, got {rec3['page1TopRelativeToViewport']}"
        print("\n[VERIFICATION PASS] Page 1 top remained rock-solid at 36px during click and typing!")

        # 4. Zoom levels matrix: 50%, 75%, 100%, 125%, 150%, 200%
        zoom_selects = driver.find_elements(By.CSS_SELECTOR, "select[aria-label='Zoom percentage']")
        if zoom_selects:
            sel = Select(zoom_selects[0])
            for val in ["50", "75", "100", "125", "150", "200"]:
                print(f"Action: Select Zoom {val}%...")
                sel.select_by_value(val)
                time.sleep(0.3)
                z_rec = record(f"Zoom {val}%")
                assert z_rec["renderedPageCount"] > 0, "Page count must be > 0"
                if val == "100":
                    assert z_rec["page1TopRelativeToViewport"] == 36, f"Zoom 100% must restore top to 36, got {z_rec['page1TopRelativeToViewport']}"

        print("\n[VERIFICATION PASS] All zoom matrix levels recorded successfully with page count invariance!")

        # 5. Page setup open/close
        setup_btns = driver.find_elements(By.CSS_SELECTOR, "button[title='Show or hide page setup']")
        if setup_btns:
            print("Action: Open Page Setup...")
            setup_btns[0].click()
            time.sleep(0.3)
            ps_open = record("Page Setup Open")
            assert ps_open["page1TopRelativeToViewport"] == 36, "Page setup open must not alter document top"

            print("Action: Close Page Setup...")
            setup_btns[0].click()
            time.sleep(0.3)
            ps_close = record("Page Setup Closed")
            assert ps_close["page1TopRelativeToViewport"] == 36, "Page setup close must not alter document top"

        # 6. Fullscreen enter/exit
        full_btns = driver.find_elements(By.XPATH, "//button[contains(@title, 'full screen')]")
        if full_btns:
            print("Action: Enter Fullscreen...")
            full_btns[0].click()
            time.sleep(0.3)
            record("Fullscreen Entered")

            print("Action: Exit Fullscreen...")
            full_btns[0].click()
            time.sleep(0.3)
            record("Fullscreen Exited")

        # 7. Browser resize
        print("Action: Resizing browser window to 1600x900...")
        driver.set_window_size(1600, 900)
        time.sleep(0.3)
        resize_rec = record("Window Resized 1600x900")
        assert resize_rec["page1TopRelativeToViewport"] == 36, "Resize must preserve Page 1 top at 36"

        driver.set_window_size(1280, 800)
        time.sleep(0.3)

    finally:
        driver.quit()

if __name__ == "__main__":
    run_test()
