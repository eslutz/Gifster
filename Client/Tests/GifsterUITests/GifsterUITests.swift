import XCTest

final class GifsterUITests: XCTestCase {
  override func setUpWithError() throws {
    continueAfterFailure = false
  }

  @MainActor
  func testContainingAppShowsHistoryAndSettings() throws {
    let app = XCUIApplication()
    app.launch()

    XCTAssertTrue(app.tabBars.buttons["Gifster"].waitForExistence(timeout: 5))
    XCTAssertTrue(app.staticTexts["Generate GIFs from text prompts inside Messages."].exists)

    app.tabBars.buttons["History"].tap()
    XCTAssertTrue(app.navigationBars["History"].waitForExistence(timeout: 2))

    app.tabBars.buttons["Settings"].tap()
    XCTAssertTrue(app.navigationBars["Settings"].waitForExistence(timeout: 2))
    XCTAssertTrue(app.textFields["Base URL"].exists)
    XCTAssertTrue(app.switches["Require App Attest"].exists)
  }

  @MainActor
  func testClearHistoryRequiresConfirmation() throws {
    let app = XCUIApplication()
    app.launchEnvironment["GIFSTER_UI_TEST_SEED_HISTORY"] = "1"
    app.launch()

    XCTAssertTrue(app.tabBars.buttons["History"].waitForExistence(timeout: 5))
    app.tabBars.buttons["History"].tap()

    XCTAssertTrue(app.staticTexts["UI test prompt"].waitForExistence(timeout: 2))
    app.buttons["Clear"].tap()

    XCTAssertTrue(app.alerts["Clear History?"].waitForExistence(timeout: 2))
    XCTAssertTrue(app.alerts["Clear History?"].buttons["Cancel"].exists)
    XCTAssertTrue(app.alerts["Clear History?"].buttons["Clear History"].exists)
  }

  @MainActor
  func testCaptureContainingAppScreenshotsForAppStorePrep() throws {
    let app = XCUIApplication()
    app.launchEnvironment["GIFSTER_UI_TEST_SEED_HISTORY"] = "1"
    app.launch()

    XCTAssertTrue(app.tabBars.buttons["Gifster"].waitForExistence(timeout: 5))
    try captureAppStoreScreenshot(named: "01-containing-app-overview")

    app.tabBars.buttons["History"].tap()
    XCTAssertTrue(app.navigationBars["History"].waitForExistence(timeout: 2))
    XCTAssertTrue(app.staticTexts["UI test prompt"].waitForExistence(timeout: 2))
    try captureAppStoreScreenshot(named: "02-containing-app-history")

    app.buttons["Clear"].tap()
    XCTAssertTrue(app.alerts["Clear History?"].waitForExistence(timeout: 2))
    try captureAppStoreScreenshot(named: "03-containing-app-clear-history")
    app.alerts["Clear History?"].buttons["Cancel"].tap()

    app.tabBars.buttons["Settings"].tap()
    XCTAssertTrue(app.navigationBars["Settings"].waitForExistence(timeout: 2))
    try captureAppStoreScreenshot(named: "04-containing-app-settings")
  }

  @MainActor
  private func captureAppStoreScreenshot(named name: String) throws {
    let screenshot = XCUIScreen.main.screenshot()
    let attachment = XCTAttachment(screenshot: screenshot)
    attachment.name = name
    attachment.lifetime = .keepAlways
    add(attachment)
  }
}
