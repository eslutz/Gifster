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
}
