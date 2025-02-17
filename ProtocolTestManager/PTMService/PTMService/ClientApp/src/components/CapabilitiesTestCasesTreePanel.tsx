﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import { MessageBar, MessageBarType } from '@fluentui/react'
import { FunctionComponent, useState } from 'react'
import CheckboxTree from 'react-checkbox-tree'
import { Icon } from '@fluentui/react/lib/Icon'
import { TestCaseFilterInfo } from '../model/CapabilitiesFileInfo'
import { useDispatch } from 'react-redux'
import { CapabilitiesConfigActions } from '../actions/CapabilitiesConfigAction'

interface CapabilitiesTestCasesTreePanelProps {
  testCases: string[]
  selectedTestCases: string[]
  filterInfo: TestCaseFilterInfo
  onChecked: (values: string[]) => void
}

const getExpandedNodes = (testCases: string[]): string[] => {
  return []
}

const getTestCaseName = (fullName: string) => {
  let name = ''
  const parts = fullName.split('.')
  if (parts.length > 0) {
    name = parts[parts.length - 1]
  }

  return name
}

const mapTestCaseToNode = (t: string) => {
  return {
    value: t, label: <span style={{ fontSize: '13px' }}>{getTestCaseName(t)}</span>, children: null, showCheckbox: true
  }
}

const createTestNodes = (testCases: string[]) => {
  const testNodes = testCases.map(t => mapTestCaseToNode(t))

  return [{ value: 'All', label: 'All', children: testNodes, showCheckbox: true }]
}

const testCasesToDisplay = (props: CapabilitiesTestCasesTreePanelProps) => {
  if (props.filterInfo.IsFiltered) {
    return props.filterInfo.FilterOutput
  } else {
    return props.testCases
  }
}

export const CapabilitiesTestCasesTreePanel: FunctionComponent<CapabilitiesTestCasesTreePanelProps> = (props) => {
  const data: any = createTestNodes(testCasesToDisplay(props))
  const expandedNodes = getExpandedNodes(props.testCases)
  const [checked, setChecked] = useState<string[]>(props.selectedTestCases)
  const [expanded, setExpanded] = useState<string[]>(expandedNodes)
  const dispatch = useDispatch()

  const trackChecked = (checked: string[], onChecked: (values: string[]) => void) => {
    setChecked(checked)
    onChecked(checked)
  }

  const onFilterRemoved = () => {
    dispatch(CapabilitiesConfigActions.removeTestCasesFilter())
  }

  return (
    props.testCases.length > 0
      ? <div style={{ border: '1px solid rgba(0, 0, 0, 0.35)', padding: '5px' }}>
              {
                  props.filterInfo.IsFiltered
                    ? <MessageBar
                        messageBarType={MessageBarType.info}
                        isMultiline={false}
                        dismissButtonAriaLabel="Close"
                        onDismiss={onFilterRemoved}>
                        {
                            <div>Filtered to -&gt; <b>{props.filterInfo.FilterBy} contains: {props.filterInfo.FilterText}</b> | Close this to remove filter.</div>
                        }
                      </MessageBar>
                    : ''
             }
             {
                  (props.filterInfo.IsFiltered && props.filterInfo.FilterOutput.length == 0)
                    ? <div>
                          <span style={{ fontSize: '13px', borderTop: '5px', borderLeft: '5px' }}>No test cases match the specified filter criteria</span>
                      </div>
                    : <CheckboxTree nodes={data}
                          checked={checked}
                          expanded={expanded}
                          onClick={curr => { }}
                          onCheck={curr => trackChecked(curr, props.onChecked)}
                          onExpand={expanded => setExpanded(expanded)}
                          showNodeIcon={false}
                          icons={{
                            check: <Icon aria-label='Checked' iconName="CheckboxComposite" />,
                            uncheck: <Icon aria-label='Not checked' iconName="Checkbox" />,
                            halfCheck: <i aria-label='Partially checked' role='img' className="ms-Icon ms-Icon--CheckboxIndeterminateCombo" />,
                            expandClose: <Icon aria-label='Collapsed' iconName="CaretHollow" />,
                            expandOpen: <Icon aria-label='Expanded' iconName="CaretSolid" />
                          }} />
             }
        </div>
      : <div>
            <span style={{ fontSize: '13px', borderTop: '5px', borderLeft: '5px' }}>No test cases found for this group/category.</span>
        </div>
  )
}
